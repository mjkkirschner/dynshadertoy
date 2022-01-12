using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Autodesk.DesignScript.Geometry;
using dynshadertoy;

namespace DynamoShaderNodes.Numerics
{
    public static class NumericExtensions
    {

       public static Vector3 ToVector3(this Autodesk.DesignScript.Geometry.Vector a)
        {
            return new Vector3((float)a.X, (float)a.Y, (float)a.Z);
        }
        public static Vector4 ToVector4(this Autodesk.DesignScript.Geometry.Vector a)
        {
            return new Vector4((float)a.X, (float)a.Y, (float)a.Z,1f);
        }

        public static System.Numerics.Matrix4x4 ToMatrix4x4(this CoordinateSystem cs)
        {
            return new System.Numerics.Matrix4x4((float)cs.XAxis.X, (float)cs.XAxis.Y, (float)cs.XAxis.Z, (float)cs.XScaleFactor,
               (float)cs.YAxis.X, (float)cs.YAxis.Y, (float)cs.YAxis.Z, (float)cs.YScaleFactor,
               (float)cs.ZAxis.X, (float)cs.ZAxis.Y, (float)cs.ZAxis.Z, (float)cs.ZScaleFactor,
                1, 1, 1, 1);
        }
        public static System.Numerics.Matrix4x4 ByArray(float[] arr)
        {
            return new System.Numerics.Matrix4x4(arr[0], arr[1], arr[2], arr[3], arr[4], arr[5], arr[6], arr[7], arr[8], arr[9], arr[10], arr[11], arr[12], arr[13], arr[14], arr[15]);
        }

        public static Matrix4x4 CreateLookAt(Vector3 cameraPosition, Vector3 cameraTarget, Vector3 cameraUpVector)
        {
            return Matrix4x4.CreateLookAt(cameraPosition, cameraTarget, cameraUpVector);
        }

        public static Matrix4x4 CreateLookAt(Autodesk.DesignScript.Geometry.Vector cameraPosition, Autodesk.DesignScript.Geometry.Vector cameraTarget, Autodesk.DesignScript.Geometry.Vector cameraUpVector)
        {
            return Matrix4x4.CreateLookAt(cameraPosition.ToVector3(), cameraTarget.ToVector3(), cameraUpVector.ToVector3());
        }

        public static Matrix4x4 CreateWorld(Vector3 position, Vector3 forward, Vector3 up)
        {
            return  Matrix4x4.CreateWorld(position, forward, up);
        }
        public static Matrix4x4 CreateWorld(Autodesk.DesignScript.Geometry.Vector position, Autodesk.DesignScript.Geometry.Vector forward, Autodesk.DesignScript.Geometry.Vector up)
        {
            return Matrix4x4.CreateWorld(position.ToVector3(), forward.ToVector3(), up.ToVector3());
        }
        public static System.Numerics.Matrix4x4 CreatePerspectiveProjection(float fov_degrees,float aspectRatio,float near, float far)
        {
            var radians = ShaderToy.DegreesToRadians(fov_degrees);
            return System.Numerics.Matrix4x4.CreatePerspectiveFieldOfView(radians, aspectRatio, near, far);
        }

        public static System.Numerics.Matrix4x4 Transpose(this Matrix4x4 mat)
        {
            return Matrix4x4.Transpose(mat);
        }


    }
}
