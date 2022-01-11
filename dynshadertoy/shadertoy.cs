using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using SharpDX.D3DCompiler;
using Veldrid;
using Veldrid.SPIRV;

namespace dynshadertoy
{
    public class ShaderToy
    {
        public struct MatrixBufferType
        {
            public Matrix4x4 world;
            public Matrix4x4 view;
            public Matrix4x4 projection;
        };

        public static Veldrid.GraphicsDevice InitializeGraphicsDevice()
        {
            //create graphics device.
            var options = new Veldrid.GraphicsDeviceOptions()
            {
                PreferStandardClipSpaceYDirection = true,
                PreferDepthRangeZeroToOne = true,
                Debug=true,
            };

            var gd = Veldrid.GraphicsDevice.CreateD3D11(options);
            Veldrid.BackendInfoD3D11 info;
            gd.GetD3D11Info(out info);
            Debug.Assert(gd.Features.ComputeShader);
            Console.WriteLine("this graphics device supports compute shaders - yay");
            return gd;
        }

        private IEnumerable<Vector4> GenerateRandomTestScene()
        {
            Console.WriteLine("Generating a random scene");
            var rand = new Random();
            var triNum = 10000;
            var triangleData = Enumerable.Range(0, (int)triNum).SelectMany(x =>
            {
                return new Vector4[]
                {
                    new Vector4(rand.Next(-10, 10), rand.Next(-10, 10), rand.Next(-10, 10),1.0f),
                       new Vector4((float)rand.NextDouble(),(float)rand.NextDouble(), (float)rand.NextDouble(),1.0f),
                    new Vector4(rand.Next(-10, 10), rand.Next(-10, 10), rand.Next(-10, 10),1.0f),
                      new Vector4((float)rand.NextDouble(),(float)rand.NextDouble(), (float)rand.NextDouble(),1.0f),
                    new Vector4(rand.Next(-10, 10), rand.Next(-10, 10), rand.Next(-10, 10),1.0f),
                      new Vector4((float)rand.NextDouble(),(float)rand.NextDouble(), (float)rand.NextDouble(),1.0f),
                };
            }).ToArray();
            return triangleData;
        }




        public static (ShaderSetDescription shaders,
            ResourceLayout resourcelayout) 
            CompileShaders(Veldrid.GraphicsDevice gd, string pixelShader, string pixelentrypoint, string vertexShader, string vertexentrypoint)
        { 
            var vd = new Veldrid.ShaderDescription(Veldrid.ShaderStages.Vertex, Encoding.ASCII.GetBytes(vertexShader),vertexentrypoint);
            var vertShader = gd.ResourceFactory.CreateShader(vd);

            var pd = new Veldrid.ShaderDescription(Veldrid.ShaderStages.Fragment, Encoding.ASCII.GetBytes(pixelShader), pixelentrypoint);
            var fragShader = gd.ResourceFactory.CreateShader(pd);

            //compile again to reflect.
            var vertshadercomp = SharpDX.D3DCompiler.ShaderBytecode.Compile(vd.ShaderBytes, vd.EntryPoint, "vs_4_0", SharpDX.D3DCompiler.ShaderFlags.None, SharpDX.D3DCompiler.EffectFlags.None, "vert");
            SharpDX.D3DCompiler.ShaderReflection sr = new SharpDX.D3DCompiler.ShaderReflection(vertshadercomp.Bytecode);
            var outputvertexElementDescriptions = new List<VertexElementDescription>();
            for (int i = 0; i < sr.Description.InputParameters; i++)
            {
                var inputparam = sr.GetInputParameterDescription(i);
                outputvertexElementDescriptions.Add(new VertexElementDescription(inputparam.SemanticName,
                    (VertexElementSemantic)Enum.Parse(typeof(VertexElementSemantic),inputparam.SemanticName,true),
                    DetermineFormatFromVertexInputParam(inputparam)));
                
            }

            //create shader layouts.
            var vertlayout = new VertexLayoutDescription(outputvertexElementDescriptions.ToArray()); /*new VertexLayoutDescription(
              new VertexElementDescription("position", VertexElementSemantic.Position, VertexElementFormat.Float4),
              new VertexElementDescription("color", VertexElementSemantic.Color, VertexElementFormat.Float4,16));
                */
            var outputResourceElements = new List<ResourceLayoutElementDescription>();
            for (int i = 0; i < sr.Description.ConstantBuffers; i++)
            {
                var cb = sr.GetConstantBuffer(i);
                //TODO for now we only support cubuffers....
                //TODO we're only parsing vertex shaders so the stage is always vertex.
                //TODO support a different flow for compute shaders...
                /* TODO - we could eventually parse the buffer types, find compatibile c# types and generate nodes or structs to hold this data
                then we could validate the data users pass in.

                for (int j = 0; j<cb.Description.VariableCount; j++)
                {
                    var cbvar = cb.GetVariable(j);
                    var type = cbvar.GetVariableType();
                    type.Description.
                }*/
                    outputResourceElements.Add(new ResourceLayoutElementDescription(cb.Description.Name, ResourceKind.UniformBuffer, ShaderStages.Vertex));

            }
            //setup buffer we'll pack with all our data.
            var reslayout = gd.ResourceFactory.CreateResourceLayout(new ResourceLayoutDescription(outputResourceElements.ToArray()));

            return (new ShaderSetDescription(new[] { vertlayout }, new[] { vertShader, fragShader }), reslayout);

        }

        private static VertexElementFormat DetermineFormatFromVertexInputParam(ShaderParameterDescription paramDesc)
        {
            // determine DXGI format
            if ((int)paramDesc.UsageMask == 1)
            {
                if (paramDesc.ComponentType == RegisterComponentType.UInt32) return VertexElementFormat.UInt1;
                else if (paramDesc.ComponentType == RegisterComponentType.SInt32) return VertexElementFormat.Int1;
                else if (paramDesc.ComponentType == RegisterComponentType.Float32) return VertexElementFormat.Float1;
            }
            else if ((int)paramDesc.UsageMask <= 3)
            {
                if (paramDesc.ComponentType == RegisterComponentType.UInt32) return VertexElementFormat.UInt2;
                else if (paramDesc.ComponentType == RegisterComponentType.SInt32) return VertexElementFormat.Int2;
                else if (paramDesc.ComponentType == RegisterComponentType.Float32) return VertexElementFormat.Float2;
            }
            else if ((int)paramDesc.UsageMask <= 7)
            {
                if (paramDesc.ComponentType == RegisterComponentType.UInt32) return VertexElementFormat.UInt3;
                else if (paramDesc.ComponentType == RegisterComponentType.SInt32) return VertexElementFormat.Int3;
                else if (paramDesc.ComponentType == RegisterComponentType.Float32) return VertexElementFormat.Float3;
            }
            else if ((int)paramDesc.UsageMask <= 15)
            {
                if (paramDesc.ComponentType == RegisterComponentType.UInt32) return VertexElementFormat.UInt4;
                else if (paramDesc.ComponentType == RegisterComponentType.SInt32) return VertexElementFormat.Int4;
                else if (paramDesc.ComponentType == RegisterComponentType.Float32) return VertexElementFormat.Float4;
            }
            throw new Exception ("could not determine format of input param");
            return new VertexElementFormat();
        }

        public byte[] Test(uint width,uint height)
        {
            var gd = InitializeGraphicsDevice();
            var resourcedata = CompileShaders(gd,pixelShaderSource, "ColorPixelShader", vertexShaderSource, "ColorVertexShader");
            var scene = GenerateRandomTestScene();

            var vertexBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription((uint)(scene.Count() *16), BufferUsage.VertexBuffer));

          
            var _uniformBuffers_vs = gd.ResourceFactory.CreateBuffer(new BufferDescription(4 * 16 * 4*100, BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            var resourceSet = gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(resourcedata.resourcelayout, _uniformBuffers_vs));

            //create target textures for offscreen rendering.
            var offscreenColor = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1,
                 PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
            var offscreenView = gd.ResourceFactory.CreateTextureView(offscreenColor);

            var offscreenColorCPU = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                 width, height, 1, 1,
                  PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));

            Texture offscreenDepth = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1, PixelFormat.R16_UNorm, TextureUsage.DepthStencil));
            var framebuffer = gd.ResourceFactory.CreateFramebuffer(new FramebufferDescription(offscreenDepth, offscreenColor));

            var graphicsPipelineDes = new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.DepthOnlyLessEqual,
                new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
                PrimitiveTopology.TriangleList,
                resourcedata.shaders,
                resourcedata.resourcelayout,
                framebuffer.OutputDescription);

            var pipeline = gd.ResourceFactory.CreateGraphicsPipeline(ref graphicsPipelineDes);

            var position = new Vector3(2, 2, 50);
            var forward = Vector3.UnitZ;

            var uniform = new MatrixBufferType();
            uniform.world = Matrix4x4.Transpose(Matrix4x4.Identity);
            uniform.view = Matrix4x4.Transpose(Matrix4x4.CreateLookAt(position, Vector3.Zero, Vector3.UnitY));
            uniform.projection = Matrix4x4.Transpose(Matrix4x4.CreatePerspectiveFieldOfView(DegreesToRadians(60f),width / (float)height, 0.1f, 256.0f));

            

            gd.UpdateBuffer(_uniformBuffers_vs, 0, ref uniform);
            gd.UpdateBuffer(vertexBuffer, 0,scene.ToArray());
            gd.WaitForIdle();
            var c1 = gd.ResourceFactory.CreateCommandList();
            c1.Begin();
            c1.SetFramebuffer(framebuffer);
            c1.SetFullViewports();
            c1.SetFullScissorRects();
            c1.ClearColorTarget(0, RgbaFloat.White);
            c1.ClearDepthStencil(1f);
            c1.SetPipeline(pipeline);
            c1.SetGraphicsResourceSet(0, resourceSet);
            c1.SetVertexBuffer(0, vertexBuffer);
            c1.Draw((uint)scene.Count()/2);
            c1.CopyTexture(offscreenColor, offscreenColorCPU);
            c1.End();
            gd.SubmitCommands(c1);
            gd.WaitForIdle();

            var mapregion = gd.Map<RgbaFloat>(offscreenColorCPU, MapMode.Read);
            var imageBytes = new byte[mapregion.SizeInBytes];
            //fill with white
            for (int i = 0; i < imageBytes.Length; i++)
            {
                imageBytes[i] = 255;
            }
            var j = 0;
            for (var i = 0; i < (mapregion.Count); i=i+1)
            {
                j = i * 4;
                imageBytes[j] = Convert.ToByte(mapregion[i].R*255.0);
                imageBytes[j+1] = Convert.ToByte(mapregion[i].G * 255.0);
                imageBytes[j+2] = Convert.ToByte(mapregion[i].B * 255.0);
                imageBytes[j+3] = Convert.ToByte(mapregion[i].A * 255.0);
              
            }
          
            gd.Unmap(offscreenColorCPU);
            return imageBytes;
        }

        public static float DegreesToRadians(float degrees)
        {
            return degrees * (float)Math.PI / 180f;
        }



        static string vertexShaderSource = @"
/////////////
// GLOBALS //
/////////////
cbuffer MatrixBuffer
{
    matrix worldMatrix;
    matrix viewMatrix;
    matrix projectionMatrix;
};
//////////////
// TYPEDEFS //
//////////////
struct VertexInputType
{
    float4 position : POSITION;
    float4 color : COLOR;
};

struct PixelInputType
{
    float4 position : SV_POSITION;
    float4 color : COLOR;
};

////////////////////////////////////////////////////////////////////////////////
// Vertex Shader
////////////////////////////////////////////////////////////////////////////////
PixelInputType ColorVertexShader(VertexInputType input)
{
    PixelInputType output;
    


    // Calculate the position of the vertex against the world, view, and projection matrices.
   output.position = mul(input.position, worldMatrix);
   output.position = mul(output.position, viewMatrix);
   output.position = mul(output.position, projectionMatrix);
    // Store the input color for the pixel shader to use.
    output.color = input.color;
    return output;
}

";
        static string pixelShaderSource = @"
struct PixelInputType
{
    float4 position : SV_POSITION;
    float4 color : COLOR;
};
////////////////////////////////////////////////////////////////////////////////
// Pixel Shader
////////////////////////////////////////////////////////////////////////////////
float4 ColorPixelShader(PixelInputType input) : SV_TARGET
{
    return input.color;
}
";

    }
}
