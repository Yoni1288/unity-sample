// using UnityEngine;
// using System.Collections;
// using Unity.InferenceEngine;

// public class SentisTest : MonoBehaviour
// {
//     public ModelAsset modelAsset;
//     public Texture2D sourceImage;
//     public Renderer targetRenderer;
//     public Material cutoutMaterial;

//     private Worker worker;
//     private Tensor<float> inputTensor;
//     private RenderTexture maskRenderTexture;
//     private RenderTexture upscaledMaskTexture;

//     void Start()
//     {
//         if (sourceImage == null || modelAsset == null)
//         {
//             Debug.LogError("Assign model and image in Inspector!");
//             return;
//         }

//         Model runtimeModel = ModelLoader.Load(modelAsset);
//         worker = new Worker(runtimeModel, BackendType.CPU);

//         inputTensor = new Tensor<float>(new TensorShape(1, 3, 256, 256));

//         maskRenderTexture = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGB32);
//         maskRenderTexture.Create();

//         upscaledMaskTexture = new RenderTexture(sourceImage.width, sourceImage.height, 0, RenderTextureFormat.ARGB32);
//         upscaledMaskTexture.Create();

//         StartCoroutine(ProcessImageCoroutine());
//     }

//     IEnumerator ProcessImageCoroutine()
//     {
//         // Convert image to tensor
//         TextureConverter.ToTensor(sourceImage, inputTensor);

//         // Run model
//         worker.Schedule(inputTensor);

//         yield return new WaitForSeconds(0.1f);

//         var outputTensor = worker.PeekOutput() as Tensor<float>;

//         if (outputTensor == null)
//         {
//             Debug.LogError("Output tensor is null!");
//             yield break;
//         }

//         Debug.Log($"Output shape: {outputTensor.shape}");

//         int width = 256;
//         int height = 256;
//         int numClasses = outputTensor.shape[1];

//         Color[] pixels = new Color[width * height];

//         for (int y = 0; y < height; y++)
//         {
//             for (int x = 0; x < width; x++)
//             {
//                 int bestClass = 0;
//                 float maxValue = float.MinValue;

//                 // ARGMAX over classes
//                 for (int c = 0; c < numClasses; c++)
//                 {
//                     float v = outputTensor[0, c, y, x];

//                     if (v > maxValue)
//                     {
//                         maxValue = v;
//                         bestClass = c;
//                     }
//                 }

//                 // Face-related classes
//                 bool isFace =
//                     bestClass == 1 ||   // skin
//                     bestClass == 4 || bestClass == 5 || // eyes
//                     bestClass == 10 ||  // nose
//                     bestClass == 11 ||  // mouth
//                     bestClass == 12 || bestClass == 13; // lips

//                 // Use confidence for smoother edges
//                 float value = isFace ? Mathf.Clamp01(maxValue) : 0f;

//                 pixels[y * width + x] = new Color(value, value, value, value);
//             }
//         }

//         // Create mask texture
//         Texture2D tempTexture = new Texture2D(width, height, TextureFormat.ARGB32, false);
//         tempTexture.SetPixels(pixels);
//         tempTexture.Apply();

//         // Copy to render texture
//         Graphics.Blit(tempTexture, maskRenderTexture);
//         Destroy(tempTexture);

//         // Upscale to match source image
//         Graphics.Blit(maskRenderTexture, upscaledMaskTexture);

//         // Optional: save debug image
//         Texture2D debugMask = new Texture2D(upscaledMaskTexture.width, upscaledMaskTexture.height, TextureFormat.ARGB32, false);
//         RenderTexture.active = upscaledMaskTexture;
//         debugMask.ReadPixels(new Rect(0, 0, upscaledMaskTexture.width, upscaledMaskTexture.height), 0, 0);
//         debugMask.Apply();
//         RenderTexture.active = null;

//         byte[] pngData = debugMask.EncodeToPNG();
//         string path = System.IO.Path.Combine(Application.persistentDataPath, "debug_mask.png");
//         System.IO.File.WriteAllBytes(path, pngData);
//         Debug.Log($"Saved mask to: {path}");

//         Destroy(debugMask);

//         // Apply to material
//         if (cutoutMaterial != null)
//         {
//             cutoutMaterial.SetTexture("_MainTex", sourceImage);
//             cutoutMaterial.SetTexture("_MaskTex", upscaledMaskTexture);

//             if (targetRenderer != null)
//             {
//                 targetRenderer.sharedMaterial = cutoutMaterial;
//             }
//         }
//     }

//     void OnDestroy()
//     {
//         worker?.Dispose();
//         inputTensor?.Dispose();

//         if (maskRenderTexture != null) maskRenderTexture.Release();
//         if (upscaledMaskTexture != null) upscaledMaskTexture.Release();
//     }
// }