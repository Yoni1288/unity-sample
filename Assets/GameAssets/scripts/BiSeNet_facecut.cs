using UnityEngine;
using System.Collections;
using Unity.InferenceEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

public class BiSeNet_facecut : MonoBehaviour
{
    public ModelAsset modelAsset;
    public Texture2D sourceImage;
    [Tooltip("Assign only the face proxy object renderer (quad/sphere), not the full character renderer.")]
    public Renderer targetRenderer;
    public Material cutoutMaterial;
    [Tooltip("Optional: character transform to follow (no bones needed).")]
    public Transform characterRoot;
    [Tooltip("World-space offset from characterRoot to place the face sphere.")]
    public Vector3 sphereOffset = new Vector3(0f, 1.8f, 0f);
    public bool followCharacter = true;
    [Header("Mask Orientation")]
    public bool flipMaskX = true;
    public bool flipMaskY = true;
    public int inputWidth = 512*20;
    public int inputHeight = 512*20;

    private Worker worker;
    private Tensor<float> inputTensor;
    private RenderTexture maskRenderTexture;
    private RenderTexture upscaledMaskTexture;
    private Material runtimeCutoutMaterial;

    void Start()
    {
        if (sourceImage == null || modelAsset == null)
        {
            Debug.LogError("Assign model and image in Inspector!");
            return;
        }

        Model runtimeModel = ModelLoader.Load(modelAsset);
        worker = new Worker(runtimeModel, BackendType.CPU);

        if (inputWidth <= 0 || inputHeight <= 0)
        {
            Debug.LogError("Input width/height must be > 0.");
            return;
        }

        inputTensor = new Tensor<float>(new TensorShape(1, 3, inputHeight, inputWidth));

        maskRenderTexture = new RenderTexture(inputWidth, inputHeight, 0, RenderTextureFormat.ARGB32);
        maskRenderTexture.Create();

        upscaledMaskTexture = new RenderTexture(sourceImage.width, sourceImage.height, 0, RenderTextureFormat.ARGB32);
        upscaledMaskTexture.Create();

        StartCoroutine(ProcessImageCoroutine());
    }

    void LateUpdate()
    {
        if (!followCharacter || characterRoot == null || targetRenderer == null)
            return;

        Transform sphereTransform = targetRenderer.transform;
        sphereTransform.position = characterRoot.position + sphereOffset;
    }

    IEnumerator ProcessImageCoroutine()
    {
        var timer = Stopwatch.StartNew();
        Debug.Log("BiSeNet: starting preprocessing");

        // Convert image to tensor
        TextureConverter.ToTensor(sourceImage, inputTensor);
        Debug.Log($"BiSeNet: tensor conversion done in {timer.ElapsedMilliseconds} ms");

        // Run model
        Debug.Log("BiSeNet: scheduling worker");
        worker.Schedule(inputTensor);
        Debug.Log($"BiSeNet: worker scheduled in {timer.ElapsedMilliseconds} ms");
        yield return null;

        Debug.Log("BiSeNet: peeking output");
        var outputTensor = worker.PeekOutput() as Tensor<float>;

        if (outputTensor == null)
        {
            Debug.LogError("Output tensor is null!");
            yield break;
        }

        // Non-blocking output readback to avoid editor stalls/hangs.
        outputTensor.ReadbackRequest();
        while (!outputTensor.IsReadbackRequestDone())
        {
            yield return null;
        }
        Debug.Log($"BiSeNet: output readback done in {timer.ElapsedMilliseconds} ms");

        Debug.Log($"Output shape: {outputTensor.shape}");

        int width = outputTensor.shape[3];
        int height = outputTensor.shape[2];
        int numClasses = outputTensor.shape[1];

        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int bestClass = 0;
                float maxLogit = float.NegativeInfinity;
                float logitDenominator = 0f;

                // Softmax confidence from logits (stable with max-shift).
                // Using raw logits directly as alpha can produce near-zero masks.
                for (int c = 0; c < numClasses; c++)
                {
                    float v = outputTensor[0, c, y, x];
                    if (v > maxLogit)
                    {
                        maxLogit = v;
                        bestClass = c;
                    }
                }

                for (int c = 0; c < numClasses; c++)
                {
                    float v = outputTensor[0, c, y, x];
                    logitDenominator += Mathf.Exp(v - maxLogit);
                }

                float bestClassProb = (logitDenominator > 0f) ? (1f / logitDenominator) : 0f;

                // Hard-coded head mask only (exclude clothing/background).
                // BiSeNet face-parsing ids typically include:
                // 1 skin, 2/3 brows, 4/5 eyes, 6 glasses, 7/8 ears, 9 earring,
                // 10 nose, 11 mouth, 12/13 lips, 14 neck, 17 hair.
                bool isFace =
                    bestClass == 1 ||
                    bestClass == 2 || bestClass == 3 ||
                    bestClass == 4 || bestClass == 5 ||
                    bestClass == 6 ||
                    bestClass == 7 || bestClass == 8 || bestClass == 9 ||
                    bestClass == 10 || bestClass == 11 ||
                    bestClass == 12 || bestClass == 13 ||
                    bestClass == 17;

                // Keep only face labels and use confidence as alpha.
                float value = isFace ? Mathf.Clamp01(bestClassProb) : 0f;

                int outX = flipMaskX ? (width - 1 - x) : x;
                int outY = flipMaskY ? (height - 1 - y) : y;
                pixels[outY * width + outX] = new Color(value, value, value, value);
            }
        }

        // Create mask texture
        Texture2D tempTexture = new Texture2D(width, height, TextureFormat.ARGB32, false);
        tempTexture.SetPixels(pixels);
        tempTexture.Apply();

        // Copy to render texture
        Graphics.Blit(tempTexture, maskRenderTexture);
        Destroy(tempTexture);

        // Upscale to match source image
        Graphics.Blit(maskRenderTexture, upscaledMaskTexture);

        // Optional: save debug image
        Texture2D debugMask = new Texture2D(upscaledMaskTexture.width, upscaledMaskTexture.height, TextureFormat.ARGB32, false);
        RenderTexture.active = upscaledMaskTexture;
        debugMask.ReadPixels(new Rect(0, 0, upscaledMaskTexture.width, upscaledMaskTexture.height), 0, 0);
        debugMask.Apply();
        RenderTexture.active = null;

        byte[] pngData = debugMask.EncodeToPNG();
        string path = System.IO.Path.Combine(Application.persistentDataPath, "debug_mask.png");
        System.IO.File.WriteAllBytes(path, pngData);
        Debug.Log($"Saved mask to: {path}");

        Destroy(debugMask);

        // Apply to material
        if (cutoutMaterial != null)
        {
            if (runtimeCutoutMaterial == null)
            {
                // Use an instance so we don't overwrite a shared asset on other objects.
                runtimeCutoutMaterial = new Material(cutoutMaterial);
            }

            runtimeCutoutMaterial.SetTexture("_MainTex", sourceImage);
            runtimeCutoutMaterial.SetTexture("_MaskTex", upscaledMaskTexture);

            if (targetRenderer != null)
            {
                targetRenderer.material = runtimeCutoutMaterial;
            }
            else
            {
                Debug.LogWarning("Target Renderer is not assigned. Assign a dedicated face proxy renderer.");
            }
        }
    }

    void OnDestroy()
    {
        worker?.Dispose();
        inputTensor?.Dispose();
        if (runtimeCutoutMaterial != null) Destroy(runtimeCutoutMaterial);

        if (maskRenderTexture != null) maskRenderTexture.Release();
        if (upscaledMaskTexture != null) upscaledMaskTexture.Release();
    }
}