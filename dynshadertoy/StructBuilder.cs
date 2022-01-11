using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Reflection.Emit;

namespace dynshadertoy
{
    public static class StructBuilder
    {
        public static object CreateNewStruct(string name, IEnumerable<Tuple<string, object>> fields)
        {
            var myType = CompileResultType(name,fields);
            return Activator.CreateInstance(myType);
        }
        private static Type CompileResultType(string name,IEnumerable<Tuple<string, object>> fields)
        {
            TypeBuilder tb = GetTypeBuilder(name);
            ConstructorBuilder constructor = tb.DefineDefaultConstructor(MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);

            // NOTE: assuming your list contains Field objects with fields FieldName(string) and FieldType(Type)
            foreach (var field in fields)
                CreateProperty(tb, field.Item1, field.Item2.GetType());

            Type objectType = tb.CreateType();
            return objectType;
        }

        private static TypeBuilder GetTypeBuilder(string name)
        {
            var typeSignature = $"{name}_{Guid.NewGuid()}";
            var an = new AssemblyName(typeSignature);
            AssemblyBuilder assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(an, AssemblyBuilderAccess.Run);
            ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");
            TypeBuilder tb = moduleBuilder.DefineType(typeSignature,
                    TypeAttributes.Public |
                    TypeAttributes.SequentialLayout |
                    TypeAttributes.Serializable, typeof(ValueType));
            return tb;
        }

        private static void CreateProperty(TypeBuilder tb, string propertyName, Type propertyType)
        {
            FieldBuilder fieldBuilder = tb.DefineField(propertyName, propertyType, FieldAttributes.Public);
        }
    }
}
