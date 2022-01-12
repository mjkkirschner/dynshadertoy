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


    public static class ShadeData
    {
        public static Bitmap ByShadersAndVertexData(object[] vertexData, uint vertexSizeInBytes ,string pixel, string vertex, IEnumerable<UniformBufferData> constantBufferData, uint width, uint height)
        {
            var gd = ShaderToy.InitializeGraphicsDevice();
            //compile the shaders.
            var resourceData = ShaderToy.CompileShaders(gd, pixel, "main", vertex, "main");
            //create buffers
            var vertexBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription((uint)(vertexSizeInBytes*vertexData.Length), BufferUsage.VertexBuffer));
            var generatedBufferStructs = new List<object>();
            var constantBuffers = new List<DeviceBuffer>();
            //we need to generate our structs here so we can figure out how large to make our constant buffer.

            foreach (var cbdata in constantBufferData)
            {   
                var generatedStruct = StructBuilder.CreateNewStruct(cbdata.Name, cbdata.FieldNames.Select((x, i) => Tuple.Create(x, cbdata.FieldValues.ElementAt(i))));
                generatedBufferStructs.Add(generatedStruct);
                var constantDeviceBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf(generatedStruct), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
                constantBuffers.Add(constantDeviceBuffer);
            }

            var resourceSet = gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(resourceData.resourcelayout, constantBuffers.ToArray()));

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

            //mutate the generated structs and update the gpu buffers.
            for (int i = 0; i < constantBufferData.Count(); i++)
            {
                var generatedStruct = generatedBufferStructs[i];
                var cbdata = constantBufferData.ElementAt(i);
                for (int k = 0; k < cbdata.FieldNames.Count(); k++)
                {
                    //box to mutate.
                    Object obj = generatedStruct;
                    var fieldInfo = generatedStruct.GetType().GetField(cbdata.FieldNames.ElementAt(k));
                    fieldInfo.SetValue(obj, cbdata.FieldValues.ElementAt(k));
                }
                //update each constant buffer with relevant user buffer struct.
                var methodinfos = gd.GetType().GetMethods().Where(x => x.Name == nameof(gd.UpdateBuffer)).ToList();
                var generic = methodinfos[0].MakeGenericMethod(generatedStruct.GetType());
                generic.Invoke(gd, new object[] { constantBuffers[i], 0U, generatedStruct });

            }

       

            var vertArr = vertexData.Cast<Vector4>().ToArray();
            var handle = GCHandle.Alloc(vertArr, GCHandleType.Pinned);
            //update the vertex buffer with our vertex data.
            gd.UpdateBuffer(vertexBuffer, 0, handle.AddrOfPinnedObject(), vertexBuffer.SizeInBytes);
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
            c1.Draw((uint)vertexData.Count() / (uint)resourceData.shaders.VertexLayouts[0].Elements.Length);
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
    }


    /// <summary>
    /// Shade Dynamo geometry as opposed to sending vertex data directly to the graphics pipeline.
    /// </summary>
    public static class ShadeGeometry
    {
        /// <summary>
        /// Pass a collection of Dynamo geometry to shader with a vertex and fragment shader. This node expects a standardized input to the vertex shader using Dynamo graphic items data.
        /// If you want to custom data, either use a constant/uniform buffer or use the other shading nodes that allow passing custom vertex buffers.
        /// </summary>
        /// <param name="geo"></param>
        /// <param name="pixel"></param>
        /// <param name="vertex">Vertex input fields are expected to be position(float4),vertcolor(float4),normal(float4),uv(float4) in that order! - no others will be supplied.
        /// <param name="constantBufferData">define a constant buffer in your vertex shader and pass it data here, order of parameters is important, names are not used. </param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        public static Bitmap ByShadersAndData(Geometry[] geo, string pixel, string vertex, IEnumerable<UniformBufferData> constantBufferData, uint width, uint height)
        {
            //get vert data
            var vertexData = geo.Select(curGeo=>Tessellate(curGeo)).SelectMany(x=>x).ToList();
            //start up dx - wonder if thi will be a problem in context of Dynamo...
            var gd = ShaderToy.InitializeGraphicsDevice();
            //compile the shaders.
            var resourceData = ShaderToy.CompileShaders(gd, pixel, "main", vertex, "main");
            //create buffers
            var vertexBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription((uint)(vertexData.Count*16), BufferUsage.VertexBuffer));

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

            var vertArr = vertexData.Cast<Vector4>().ToArray();
            var handle = GCHandle.Alloc(vertArr, GCHandleType.Pinned);
            //update the vertex buffer with our vertex data.
            gd.UpdateBuffer(vertexBuffer, 0, handle.AddrOfPinnedObject(),vertexBuffer.SizeInBytes);
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
            c1.Draw((uint)vertexData.Count()/4);
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

        
        public static List<object> Tessellate(Geometry geo)
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

            List<List<float>> colorData = Split(rp.Colors?.Select(c => (float)c).ToList(), 4);
            //if no vert colors default to black.
            if(colorData == null)
            {
                colorData = verts.Select(x => new List<float>() {0f,0f,0f,0f}).ToList();
            }
            List<Vector4> colors = colorData.Select(x => new Vector4(x[0], x[1], x[2], x[3])).ToList();

            List<List<double>> normalData = Split(rp.MeshNormals.ToList(), 3);
            List<Vector4> normals = normalData.Select(x => new Vector4((float)x[0], (float)x[1], (float)x[2], 1.0f)).ToList();

            List<List<double>> uvData = Split(rp.MeshTextureCoordinates?.ToList(), 2);
            if (uvData == null)
            {
                uvData = verts.Select(x => new List<double>() { 0f, 0f, 0f, 0f }).ToList();
            }
            List<Vector4> uvs = uvData.Select(x => new Vector4((float)x[0], (float)x[1], 0f,0f)).ToList();

            var output = new List<object>();
            for (int i = 0; i < verts.Count; i++)
            {
                output.Add(verts[i]);
                output.Add(colors[i]);
                output.Add(normals[i]);
                output.Add(uvs[i]);
            }
            return output;

            //TODO DISPOSE unmanaged resources!!!!
        }

        public static List<List<T>> Split<T>(List<T> source, int subListLength)
        {
            return source?.
               Select((x, i) => new { Index = i, Value = x })
               .GroupBy(x => x.Index / subListLength)
               .Select(x => x.Select(v => v.Value).ToList())
               .ToList();
        }

    }
}
