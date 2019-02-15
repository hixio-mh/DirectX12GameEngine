﻿using System;
using System.Collections.Generic;
using System.Linq;
using DirectX12GameEngine.Graphics;
using DirectX12GameEngine.Rendering.Shaders;
using SharpDX.Direct3D12;
using SharpDX.DXGI;

using PipelineState = DirectX12GameEngine.Graphics.PipelineState;

namespace DirectX12GameEngine.Rendering.Materials
{
    public class MaterialGeneratorContext
    {
        private readonly Stack<IMaterialDescriptor> materialDescriptorStack = new Stack<IMaterialDescriptor>();

        public MaterialGeneratorContext(GraphicsDevice device, Material material)
        {
            GraphicsDevice = device;
            Material = material;
        }

        public GraphicsDevice GraphicsDevice { get; }

        public Material Material { get; }

        public IList<Texture> ConstantBuffers { get; } = new List<Texture>();

        public IList<Texture> Samplers { get; } = new List<Texture>();

        public IList<Texture> Textures { get; } = new List<Texture>();

        public IMaterialDescriptor? MaterialDescriptor => materialDescriptorStack.Count > 0 ? materialDescriptorStack.Peek() : null;

        public MaterialPass? MaterialPass { get; private set; }

        public int PassCount { get; private set; } = 1;

        public int PassIndex { get; private set; }

        public void PushMaterialDescriptor(IMaterialDescriptor descriptor)
        {
            materialDescriptorStack.Push(descriptor);
        }

        public IMaterialDescriptor? PopMaterialDescriptor()
        {
            return materialDescriptorStack.Count > 0 ? materialDescriptorStack.Pop() : null;
        }

        public MaterialPass PushPass()
        {
            MaterialPass materialPass = new MaterialPass { PassIndex = PassIndex };
            Material.Passes.Add(materialPass);

            MaterialPass = materialPass;

            return materialPass;
        }

        public MaterialPass? PopPass()
        {
            PassIndex++;

            MaterialPass? materialPass = MaterialPass;
            MaterialPass = null;

            Textures.Clear();

            return materialPass;
        }

        public (CpuDescriptorHandle, GpuDescriptorHandle) CopyDescriptors(IList<Texture> resources)
        {
            if (resources.Count == 0) return default;

            int[] srcDescriptorRangeStarts = new int[resources.Count];

            for (int i = 0; i < srcDescriptorRangeStarts.Length; i++)
            {
                srcDescriptorRangeStarts[i] = 1;
            }

            var (cpuDescriptorHandle, gpuDescriptorHandle) = GraphicsDevice.ShaderResourceViewAllocator.Allocate(resources.Count);

            GraphicsDevice.NativeDevice.CopyDescriptors(
                1, new[] { cpuDescriptorHandle }, new[] { resources.Count },
                resources.Count, resources.Select(t => t.NativeCpuDescriptorHandle).ToArray(), srcDescriptorRangeStarts,
                DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);

            return (cpuDescriptorHandle, gpuDescriptorHandle);
        }

        public PipelineState CreateGraphicsPipelineState()
        {
            if (MaterialDescriptor is null) throw new InvalidOperationException("The current material descriptor cannot be null when creating a pipeline state.");

            InputElement[] inputElements = new[]
            {
                new InputElement("Position", 0, Format.R32G32B32_Float, 0),
                new InputElement("Normal", 0, Format.R32G32B32_Float, 1),
                new InputElement("TexCoord", 0, Format.R32G32_Float, 2)
            };

            ShaderGenerationResult result = ShaderGenerator.GenerateShaderSource(MaterialDescriptor.Attributes);

            if (result.ShaderSource is null) throw new Exception("Shader source cannot be null.");

            (ShaderBytecode VertexShader, ShaderBytecode PixelShader, ShaderBytecode HullShader, ShaderBytecode DomainShader, ShaderBytecode GeometryShader) shaders = default;

            shaders.VertexShader = result.VertexShader is null ? throw new Exception("Vertex shader must be present.") : ShaderCompiler.CompileShaderLegacy(result.ShaderSource, SharpDX.D3DCompiler.ShaderVersion.VertexShader, result.VertexShader.Name);
            shaders.PixelShader = result.PixelShader is null ? throw new Exception("Pixel shader must be present.") : ShaderCompiler.CompileShaderLegacy(result.ShaderSource, SharpDX.D3DCompiler.ShaderVersion.PixelShader, result.PixelShader.Name);
            shaders.HullShader = result.HullShader is null ? default : ShaderCompiler.CompileShader(result.ShaderSource, SharpDX.D3DCompiler.ShaderVersion.PixelShader, result.HullShader.Name);
            shaders.DomainShader = result.DomainShader is null ? default : ShaderCompiler.CompileShader(result.ShaderSource, SharpDX.D3DCompiler.ShaderVersion.PixelShader, result.DomainShader.Name);
            shaders.GeometryShader = result.GeometryShader is null ? default : ShaderCompiler.CompileShader(result.ShaderSource, SharpDX.D3DCompiler.ShaderVersion.PixelShader, result.GeometryShader.Name);

            RootSignature rootSignature = CreateRootSignature();

            return new PipelineState(GraphicsDevice, inputElements, rootSignature,
                shaders.VertexShader, shaders.PixelShader, shaders.HullShader, shaders.DomainShader, shaders.GeometryShader);
        }

        public RootSignature CreateRootSignature()
        {
            List<RootParameter> rootParameters = new List<RootParameter>
            {
                    new RootParameter(ShaderVisibility.All,
                        new RootConstants(0, 0, 1)),
                    new RootParameter(ShaderVisibility.All,
                        new DescriptorRange(DescriptorRangeType.ConstantBufferView, 1, 1)),
                    new RootParameter(ShaderVisibility.All,
                        new DescriptorRange(DescriptorRangeType.ConstantBufferView, 1, 2)),
                    new RootParameter(ShaderVisibility.All,
                        new DescriptorRange(DescriptorRangeType.ConstantBufferView, 1, 3))
            };

            if (ConstantBuffers.Count > 0)
            {
                rootParameters.Add(new RootParameter(ShaderVisibility.All,
                    new DescriptorRange(DescriptorRangeType.ConstantBufferView, ConstantBuffers.Count, 4)));
            }

            if (Samplers.Count > 0)
            {
                rootParameters.Add(new RootParameter(ShaderVisibility.All,
                    new DescriptorRange(DescriptorRangeType.Sampler, Samplers.Count, 1)));
            }

            if (Textures.Count > 0)
            {
                rootParameters.Add(new RootParameter(ShaderVisibility.All,
                    new DescriptorRange(DescriptorRangeType.ShaderResourceView, Textures.Count, 0)));
            }

            StaticSamplerDescription[] staticSamplers = new StaticSamplerDescription[]
            {
                new StaticSamplerDescription(ShaderVisibility.All, 0, 0)
                {
                    Filter = Filter.MinLinearMagMipPoint
                }
            };

            RootSignatureDescription rootSignatureDescription = new RootSignatureDescription(
                RootSignatureFlags.AllowInputAssemblerInputLayout, rootParameters.ToArray(), staticSamplers);

            return GraphicsDevice.CreateRootSignature(rootSignatureDescription);
        }
    }
}
