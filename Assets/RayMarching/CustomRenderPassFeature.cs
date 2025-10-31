using System.Collections;
using System.Collections.Generic;
using Unity.IO.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class CustomRenderPassFeature : ScriptableRendererFeature
{
    [SerializeField]
    public Material m_Material = null;
    public class CustomRenderPass : ScriptableRenderPass
    {
        ProfilingSampler m_ProfilingSampler = new ProfilingSampler("VolumetricLight");
        private Material material;
        RTHandle m_CameraColorTarget;
        public CustomRenderPass(Material mat)
        {
            material = mat;
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        }
        public void SetTarget(RTHandle colorHandle)
        {
            m_CameraColorTarget = colorHandle;
        }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ConfigureTarget(m_CameraColorTarget);
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // 材质检查已在AddRenderPasses中完成
            if (material == null || m_CameraColorTarget == null)
                return;
            
            CommandBuffer cmd = CommandBufferPool.Get("VolumetricLight");
            
            // 体积光渲染
            Blitter.BlitCameraTexture(cmd, m_CameraColorTarget, m_CameraColorTarget, material, 0);
            
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
    private CustomRenderPass m_RenderPass;

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        if (m_RenderPass == null)
            return;
        
        // 设置渲染目标
        m_RenderPass.SetTarget(renderer.cameraColorTargetHandle);
    }

    public override void Create()
    {
        m_RenderPass = new CustomRenderPass(m_Material);
    }
    
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // 检查是否是Game相机
        if (renderingData.cameraData.camera.cameraType != CameraType.Game)
            return;
        
        // 检查材质是否存在
        if (m_Material == null || m_RenderPass == null)
            return;
        
        renderer.EnqueuePass(m_RenderPass);
    }
}