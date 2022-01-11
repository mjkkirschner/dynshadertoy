using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Autodesk.DesignScript.Geometry;
using Autodesk.DesignScript.Interfaces;
using Dynamo.Visualization;
using dynshadertoy;
using Veldrid;
using static dynshadertoy.ShaderToy;

namespace DynamoShaderNodes
{


    public class UniformBufferData
    {
        public string Name { get; set; }
        public IEnumerable<string> FieldNames { get; set; }
        public IEnumerable<object> FieldValues { get; set; }
        //TODO constrain this somehow to types we support...
        public static UniformBufferData ByNameAndDataList(string name, IEnumerable<string> fieldNames, IEnumerable<object> fieldValues)
        {
            return new UniformBufferData() { FieldValues = fieldValues, FieldNames = fieldNames, Name = name };
        }
    }

    /// <summary>
    /// Shade Dynamo geometry as opposed to sending vertex data directly to the graphics pipeline.
    /// </summary>
    public static class ShadeGeometry
    {
        public static Bitmap ByShadersAndData(Geometry geo, string pixel, string vertex, IEnumerable<UniformBufferData> constantBufferData, uint width, uint height)
        {
            //get vert data
            var verts = Tessellate(geo);
            //start up dx - wonder if thi will be a problem in context of Dynamo...
            var gd = ShaderToy.InitializeGraphicsDevice();
            //compile the shaders.
            var resourceData = ShaderToy.CompileShaders(gd, pixel, "main", vertex, "main");

            //create buffers
            var vertexBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription((uint)(verts.Count * 16), BufferUsage.VertexBuffer));

            //TODO create constant buffers by parsing the constant buffers in the shader...
            //also calculate size based on variables in buffer or size of struct?
            var _uniformBuffers_vs = gd.ResourceFactory.CreateBuffer(new BufferDescription(4 * 16 * 4, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            var resourceSet = gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(resourceData.resourcelayout, _uniformBuffers_vs));

            //create target textures for offscreen rendering.
            var offscreenColor = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1,
                 PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
            var offscreenView = gd.ResourceFactory.CreateTextureView(offscreenColor);

            //we need to copy the resulting offscreen render target back to a texture the CPU can read, so we can render in wpf.
            var offscreenColorCPU = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                 width, height, 1, 1,
                  PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));

            //depth buffer target.
            Texture offscreenDepth = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1, PixelFormat.R16_UNorm, TextureUsage.DepthStencil));
            var framebuffer = gd.ResourceFactory.CreateFramebuffer(new FramebufferDescription(offscreenDepth, offscreenColor));

            //finally create the pipeline using our two shaders.

            var graphicsPipelineDes = new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.DepthOnlyLessEqual,
                new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
                PrimitiveTopology.TriangleList,
                resourceData.shaders,
                resourceData.resourcelayout,
                framebuffer.OutputDescription);

            var pipeline = gd.ResourceFactory.CreateGraphicsPipeline(ref graphicsPipelineDes);

            //parse the user buffer data and generate structs for each one.
            //TODO - we don't support recursive generation yet.
            foreach (var cbdata in constantBufferData)
            {
                var generatedStruct = StructBuilder.CreateNewStruct(cbdata.Name, cbdata.FieldNames.Select((x, i) => Tuple.Create(x, cbdata.FieldValues.ElementAt(i))));
                for (int i = 0; i < cbdata.FieldNames.Count(); i++)
                {
                    Object obj = generatedStruct;
                    var fieldInfo = generatedStruct.GetType().GetField(cbdata.FieldNames.ElementAt(i));
                    fieldInfo.SetValue(obj, cbdata.FieldValues.ElementAt(i));
                }
                //update each constant buffer with relevant user buffer struct.
                var methodinfos = gd.GetType().GetMethods().Where(x => x.Name == nameof(gd.UpdateBuffer)).ToList();
                var generic = methodinfos[0].MakeGenericMethod(generatedStruct.GetType());
                //TODO this only works with one buffer right now.
                generic.Invoke(gd, new object[] { _uniformBuffers_vs, 0U, generatedStruct });
            }

            //update the vertex buffer with our vertex data.
            gd.UpdateBuffer(vertexBuffer, 0, verts.ToArray());
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
            c1.Draw((uint)verts.Count());
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
            for (var i = 0; i < (mapregion.Count); i = i + 1)
            {
                j = i * 4;
                imageBytes[j] = Convert.ToByte(mapregion[i].R * 255.0);
                imageBytes[j + 1] = Convert.ToByte(mapregion[i].G * 255.0);
                imageBytes[j + 2] = Convert.ToByte(mapregion[i].B * 255.0);
                imageBytes[j + 3] = Convert.ToByte(mapregion[i].A * 255.0);
            }

            gd.Unmap(offscreenColorCPU);
            var bmp = new Bitmap((int)width, (int)height, (int)width * 4, System.Drawing.Imaging.PixelFormat.Format32bppRgb,
                    Marshal.UnsafeAddrOfPinnedArrayElement(imageBytes, 0));
            return bmp;
        }

        private static List<Vector4> Tessellate(Geometry geo)
        {
            //first get the mesh data from the geometry.
            var rpfactory = new DefaultRenderPackageFactory();
            var rp = rpfactory.CreateRenderPackage();
            var tp = new TessellationParameters();
            tp.MaxTessellationDivisions = 128;
            tp.Tolerance = .01;

            geo.Tessellate(rp, tp);
            //grab double components from rp and subset them into verts
            //TODO grab other data, like, norms, colors, uvs etc and enforce a standard vertex layout
            //complain if the shader is modified?
            List<List<double>> vertData = Split(rp.MeshVertices.ToList(), 3);
            List<Vector4> verts = vertData.Select(x => new Vector4((float)x[0], (float)x[1], (float)x[2], 1.0f)).ToList();
            return verts;
        }

        public static List<List<T>> Split<T>(List<T> source, int subListLength)
        {
            return source.
               Select((x, i) => new { Index = i, Value = x })
               .GroupBy(x => x.Index / subListLength)
               .Select(x => x.Select(v => v.Value).ToList())
               .ToList();
        }

    }
}
