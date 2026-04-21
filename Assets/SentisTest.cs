using UnityEngine;
using System.Collections;
using Unity.InferenceEngine; // ה-Namespace שעובד אצלך

public class SentisTest : MonoBehaviour
{
    public ModelAsset modelAsset;
    public Texture2D sourceImage; 
    public Renderer targetRenderer;
    public Material cutoutMaterial;
    
    private Worker worker;
    private Tensor<float> inputTensor;
    private RenderTexture maskRenderTexture;
    private RenderTexture upscaledMaskTexture;

    void Start()
    {
        Debug.LogError("Yoni 123");
        if (sourceImage == null || modelAsset == null)
        {
            Debug.LogError("וודא שגררת גם מודל וגם תמונה ל-Inspector!");
            return;
        }
        
        Debug.Log($"Source image size: {sourceImage.width}x{sourceImage.height}");
        Debug.Log($"Cutout material assigned: {(cutoutMaterial != null ? "Yes" : "No")}");

        // 1. טעינת המודל
        Model runtimeModel = ModelLoader.Load(modelAsset);

        // 2. יצירת ה-Worker בשיטה החדשה (CPU הכי בטוח ל-Mac Intel)
        worker = new Worker(runtimeModel, BackendType.CPU);
        
        // 3. יצירת טנסור קלט במידות 256x256
        inputTensor = new Tensor<float>(new TensorShape(1, 3, 256, 256));

        // 4. הכנת ה-RenderTexture למסיכה
        maskRenderTexture = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGB32);
        maskRenderTexture.Create();
        
        // 5. הכנת ה-RenderTexture לעלייה בהגדלה (כדי שתתאים לתמונת המקור)
        upscaledMaskTexture = new RenderTexture(sourceImage.width, sourceImage.height, 0, RenderTextureFormat.ARGB32);
        upscaledMaskTexture.Create();

        ProcessImage();
    }
    
void ProcessImage()
{
    StartCoroutine(ProcessImageCoroutine());
}

IEnumerator ProcessImageCoroutine()
{
    Debug.Log("Coroutine started");
    
    // המרה לטנסור
    TextureConverter.ToTensor(sourceImage, inputTensor);
    Debug.Log("Tensor conversion complete");
    
    // הרצה
    worker.Schedule(inputTensor);
    Debug.Log("Worker scheduled");
    
    // Wait for the async operation to complete
    yield return new WaitForSeconds(0.1f);
    Debug.Log("Wait complete, getting output");
    
    try
    {
        // שליפת הפלט
        var outputTensor = worker.PeekOutput() as Tensor<float>;
        Debug.Log("Output tensor retrieved");

        if (outputTensor != null)
        {
            // Debug: log tensor shape
            Debug.Log($"Output tensor shape: {outputTensor.shape}");
            
            // Debug: print sample tensor values
            Debug.Log($"Sample tensor values:");
            for (int y = 0; y < 5; y++)
            {
                for (int x = 0; x < 5; x++)
                {
                    float value = outputTensor[0, 0, y, x];
                    Debug.Log($"  [{y},{x}] = {value}");
                }
            }
            
            // Manually convert tensor to texture
            // Tensor shape is (1, 1, 256, 256) = (batch, channels, height, width)
            int width = 256;
            int height = 256;
            Color[] pixels = new Color[width * height];
            
            // Read tensor data (reversed for 180 degree flip)
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Get value from tensor, but flip 180 degrees by reversing indices
                    float value = outputTensor[0, 0, height - 1 - y, x]; 
                    value = Mathf.Clamp01(value); 
                    pixels[y * width + x] = new Color(value, value, value, value);
                }
            }
            
            // Create temporary texture from pixels
            Texture2D tempTexture = new Texture2D(width, height, TextureFormat.ARGB32, false);
            tempTexture.SetPixels(pixels);
            tempTexture.Apply();
            
            // Post-process the mask: Create a face-focused radial mask
            Color[] processedPixels = tempTexture.GetPixels();
            
            // FIRST: Remove everything below 50% height (shoulders and body)
            int cutoffY = (int)(height * 0.5f);
            for (int y = cutoffY; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    processedPixels[y * width + x].a = 0f; // Make fully transparent
                }
            }
            


            // Apply aggressive threshold - only keep high confidence pixels
            for (int i = 0; i < processedPixels.Length; i++)
            {
                float alpha = processedPixels[i].a;
                // Keep only pixels > 0.7 - this removes faint background
                processedPixels[i].a = alpha > 0.7f ? 1f : 0f;
            }
            
            tempTexture.SetPixels(processedPixels);
            tempTexture.Apply(); // THIS WAS MISSING!
            
            // Copy to render texture
            Graphics.Blit(tempTexture, maskRenderTexture);
            Destroy(tempTexture);
            
            // Upscale mask to match source image size
            Graphics.Blit(maskRenderTexture, upscaledMaskTexture);
            
            // Debug: save mask texture to file
            Texture2D debugMask = new Texture2D(upscaledMaskTexture.width, upscaledMaskTexture.height, TextureFormat.ARGB32, false);
            RenderTexture.active = upscaledMaskTexture;
            debugMask.ReadPixels(new Rect(0, 0, upscaledMaskTexture.width, upscaledMaskTexture.height), 0, 0);
            debugMask.Apply();
            RenderTexture.active = null;
            
            byte[] pngData = debugMask.EncodeToPNG();
            string debugPath = System.IO.Path.Combine(Application.persistentDataPath, "debug_mask.png");
            System.IO.File.WriteAllBytes(debugPath, pngData);
            Debug.Log($"Debug mask saved to: {debugPath}");
            
            // Check min/max values
            Color[] debugPixels = debugMask.GetPixels();
            float minAlpha = 1f, maxAlpha = 0f;
            foreach (Color p in debugPixels)
            {
                minAlpha = Mathf.Min(minAlpha, p.a);
                maxAlpha = Mathf.Max(maxAlpha, p.a);
            }
            Debug.Log($"Mask alpha range: {minAlpha} to {maxAlpha}");
            Destroy(debugMask);
            
            if (cutoutMaterial != null)
            {
                cutoutMaterial.SetTexture("_MainTex", sourceImage);
                cutoutMaterial.SetTexture("_MaskTex", upscaledMaskTexture);
                
                Debug.Log("Mask texture set to material");
                
                if (targetRenderer != null)
                {
                    targetRenderer.sharedMaterial = cutoutMaterial;
                    Debug.Log("Material applied to renderer");
                }
            }
            else
            {
                Debug.LogError("Cutout material is null!");
            }
        }
        else
        {
            Debug.LogError("Output tensor is null!");
        }
    }
    catch (System.Exception e)
    {
        Debug.LogError($"Error in ProcessImageCoroutine: {e}");
    }
}

    void OnDestroy()
    {
        worker?.Dispose();
        inputTensor?.Dispose();
        if (maskRenderTexture != null) maskRenderTexture.Release();
        if (upscaledMaskTexture != null) upscaledMaskTexture.Release();
    }
}