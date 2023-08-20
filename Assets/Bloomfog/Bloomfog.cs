using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class Bloomfog : ScriptableRendererFeature
{
    //These are static fields for graphics settings to access
    public static bool Enabled = true;
    public static int Downsample = 2;
    public static int BlurPasses = 16;

    [System.Serializable]
    public class BloomFogSettings
    {
        public Material thresholdMaterial;

        [Space]
        public float bloomCaptureExtraFov = 0f;
        public float threshold = 1f;
        public float brightnessMult = 1f;
        public float attenuation = 1f;
        public float fogOffset = 0f;

        [Header("Blur Settings")]
        public int referenceScreenHeight = 720;
        [Min(2)] public int blurPasses = 2;
        [Min(1)] public int downsample = 1;
        public Material blurMaterial;

        [Header("Output Settings")]
        public Material outputMaterial;
        public string outputTextureName;

        [System.NonSerialized] public int textureWidth;
        [System.NonSerialized] public int textureHeight;
    }

    [SerializeField] private BloomFogSettings settings = new BloomFogSettings();

    private CameraConfigPass cameraConfigPass;
    private BloomFogPass bloomFogPass;


    public override void Create()
    {
        Downsample = settings.downsample;
        BlurPasses = settings.blurPasses;

        cameraConfigPass = new CameraConfigPass(settings);
        cameraConfigPass.renderPassEvent = RenderPassEvent.BeforeRendering;

        bloomFogPass = new BloomFogPass(settings);
        bloomFogPass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;

        Shader.SetGlobalFloat("_CustomFogAttenuation", settings.attenuation);
        Shader.SetGlobalFloat("_CustomFogOffset", settings.fogOffset);
    }


    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if(settings.blurMaterial && settings.outputMaterial && settings.thresholdMaterial)
        {
            renderer.EnqueuePass(cameraConfigPass);

            bloomFogPass.SourceTexture = renderer.cameraColorTarget;
            renderer.EnqueuePass(bloomFogPass);
        }
    }


    private class CameraConfigPass : ScriptableRenderPass
    {
        private BloomFogSettings settings;

        
        public CameraConfigPass(BloomFogSettings fogSettings)
        {
            settings = fogSettings;
        }


        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Camera mainCamera = Camera.main;
            Camera renderCamera = renderingData.cameraData.camera;

            //Update the camera field of view
            renderCamera.fieldOfView = Mathf.Clamp(mainCamera.fieldOfView + settings.bloomCaptureExtraFov, 30, 160);

            float verticalFov = Mathf.Deg2Rad * renderCamera.fieldOfView;
            float horizontalFov = 2 * Mathf.Atan(Mathf.Tan(verticalFov / 2) * renderCamera.aspect);

            //Calculate the new texture ratio based on camera fov
            float originalVertFov = Mathf.Deg2Rad * mainCamera.fieldOfView;
            float screenPlaneDistance = (settings.referenceScreenHeight / 2) / Mathf.Tan(originalVertFov / 2);

            //Set the new texture size
            settings.textureWidth = Mathf.RoundToInt(Mathf.Tan(horizontalFov / 2) * screenPlaneDistance * 2);
            settings.textureHeight = Mathf.RoundToInt(Mathf.Tan(verticalFov / 2) * screenPlaneDistance * 2);

            float referenceWidth = settings.referenceScreenHeight * mainCamera.aspect;
            float widthRatio = referenceWidth / settings.textureWidth;
            float heightRatio = (float)settings.referenceScreenHeight / settings.textureHeight;

            // Debug.Log($"fov: {verticalFov} horizontal: {horizontalFov} width: {settings.textureWidth} height: {settings.textureHeight} ratio: {widthRatio}, {heightRatio}");

            Shader.SetGlobalVector("_FogTextureToScreenRatio", new Vector2(widthRatio, heightRatio));
        }
    }


    private class BloomFogPass : ScriptableRenderPass
    {
        public RenderTargetIdentifier SourceTexture;

        private BloomFogSettings settings;

        private LayerMask environmentLayerMask;
        private Material environmentMaskMaterial;
        private Material thresholdMaterial;
        private float threshold;
        private float brightnessMult;

        private Material blurMaterial;
        private int referenceHeight;

        private string outputTextureName;
        private Material outputMaterial;

        private int tempID1;
        private int tempID2;

        private int maskID;

        private RenderTargetIdentifier tempRT1;
        private RenderTargetIdentifier tempRT2;

        private RenderTargetIdentifier maskRT;


        public BloomFogPass(BloomFogSettings fogSettings)
        {
            settings = fogSettings;

            thresholdMaterial = fogSettings.thresholdMaterial;
            threshold = fogSettings.threshold;
            brightnessMult = fogSettings.brightnessMult;

            blurMaterial = fogSettings.blurMaterial;
            referenceHeight = fogSettings.referenceScreenHeight;
            outputTextureName = fogSettings.outputTextureName;

            outputMaterial = fogSettings.outputMaterial;
        }


        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            int width = settings.textureWidth / Bloomfog.Downsample;
            int height = settings.textureHeight / Bloomfog.Downsample;

            //Create our temporary render textures for blurring
            tempID1 = Shader.PropertyToID("tempBlurRT1");
            tempID2 = Shader.PropertyToID("tempBlurRT2");
            cmd.GetTemporaryRT(tempID1, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.DefaultHDR);
            cmd.GetTemporaryRT(tempID2, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.DefaultHDR);

            tempRT1 = new RenderTargetIdentifier(tempID1);
            tempRT2 = new RenderTargetIdentifier(tempID2);
        }


        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if(!Enabled)
            {
                if(!string.IsNullOrEmpty(outputTextureName))
                {
                    //Bloomfog shouldn't be used, just output a black texture
                    Shader.SetGlobalTexture(outputTextureName, Texture2D.blackTexture);
                }
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get("KawaseBlur");

            //Copy the source into the first temp texture, applying brightness threshold
            cmd.SetGlobalFloat("_Threshold", threshold);
            cmd.SetGlobalFloat("_BrightnessMult", brightnessMult);
            cmd.Blit(SourceTexture, tempRT1, thresholdMaterial);

            for(int i = 0; i < Bloomfog.BlurPasses - 1; i++)
            {
                //Copy between temp textures, blurring more each time
                cmd.SetGlobalFloat("_Offset", 0.5f + i);
                cmd.Blit(tempRT1, tempRT2, blurMaterial);

                RenderTargetIdentifier tempSwap = tempRT1;
                tempRT1 = tempRT2;
                tempRT2 = tempSwap;
            }

            //Final pass, outputting final blurred result
            cmd.SetGlobalFloat("_Offset", 0.5f + Bloomfog.BlurPasses - 1);
            if(string.IsNullOrEmpty(outputTextureName))
            {
                cmd.Blit(tempRT1, SourceTexture, outputMaterial);
            }
            else
            {
                cmd.Blit(tempRT1, tempRT2, blurMaterial);
                cmd.SetGlobalTexture(outputTextureName, tempRT2);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CommandBufferPool.Release(cmd);
        }
    }
}