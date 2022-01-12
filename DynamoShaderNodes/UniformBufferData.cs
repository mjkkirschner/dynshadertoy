using System.Collections.Generic;

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
}
