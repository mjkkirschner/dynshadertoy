using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Veldrid;

namespace dynshadertoy
{
    public class shadertoy
    {
        public struct MatrixBufferType
        {
            public Matrix4x4 world;
            public Matrix4x4 view;
            public Matrix4x4 projection;
        };

        public Veldrid.GraphicsDevice InitializeGraphicsDevice()
        {
            //create graphics device.
            var options = new Veldrid.GraphicsDeviceOptions()
            {
                PreferStandardClipSpaceYDirection = true,
                PreferDepthRangeZeroToOne = true,
                Debug=true,
            };

            var gd = Veldrid.GraphicsDevice.CreateD3D11(options);
            Veldrid.BackendInfoVulkan info;
            gd.GetVulkanInfo(out info);
            Debug.Assert(gd.Features.ComputeShader);
            Console.WriteLine("this graphics device supports compute shaders - yay");
            return gd;
        }

        public IEnumerable<Vector4> GenerateRandomTestScene()
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

        public ShaderSetDescription CompileTestShaders(Veldrid.GraphicsDevice gd)
        {
            var vertexShaderSource = @"
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
            var pixelShaderSource = @"
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

            var vd = new Veldrid.ShaderDescription(Veldrid.ShaderStages.Vertex, Encoding.ASCII.GetBytes(vertexShaderSource), "ColorVertexShader");
            var vertShader = gd.ResourceFactory.CreateShader(vd);

            var pd = new Veldrid.ShaderDescription(Veldrid.ShaderStages.Fragment, Encoding.ASCII.GetBytes(pixelShaderSource), "ColorPixelShader");
            var fragShader = gd.ResourceFactory.CreateShader(pd);



            //create shader layouts.
            var vertlayout = new VertexLayoutDescription(
              new VertexElementDescription("position", VertexElementSemantic.Position, VertexElementFormat.Float4),
              new VertexElementDescription("color", VertexElementSemantic.Color, VertexElementFormat.Float4,16));

             return new ShaderSetDescription(new[] { vertlayout }, new[] { vertShader, fragShader });

        }

        public byte[] Test(uint width,uint height)
        {
            var gd = InitializeGraphicsDevice();
            var shaders = CompileTestShaders(gd);
            var scene = GenerateRandomTestScene();

            var vertexBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription((uint)(scene.Count() *16), BufferUsage.VertexBuffer));

            //setup buffer we'll pack with all our data.
            var reslayout = gd.ResourceFactory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("UBO", ResourceKind.UniformBuffer, ShaderStages.Vertex)));
            var _uniformBuffers_vs = gd.ResourceFactory.CreateBuffer(new BufferDescription(4 * 16 * 4*100, BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            var resourceSet = gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(reslayout, _uniformBuffers_vs));

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
                shaders,
                reslayout,
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

    }
}
