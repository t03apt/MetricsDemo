using Avro.IO;
using Avro.Specific;
using Confluent.Kafka;

namespace Contracts.Serializers;

public class AvroSerializer<T> : IAsyncSerializer<T>, IDeserializer<T>
    where T : ISpecificRecord, new()
{
    public static byte[] Serialize(T objectToSerialize)
    {
        var writer = new SpecificDefaultWriter(objectToSerialize.Schema); // Schema comes from pre-compiled, code-gen phase
        using var ms = new MemoryStream();
        writer.Write(objectToSerialize, new BinaryEncoder(ms));
        return ms.ToArray();
    }

    public static T Deserialize(byte[] serialized)
    {
        var regenObj = new T();
        var reader = new SpecificDefaultReader(regenObj.Schema, regenObj.Schema);
        using var ms = new MemoryStream(serialized);
        reader.Read(regenObj, new BinaryDecoder(ms));
        return regenObj;
    }

    public Task<byte[]> SerializeAsync(T data, SerializationContext context)
    {
        return Task.FromResult(Serialize(data));
    }

    public T Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
    {
        return Deserialize(data.ToArray());
    }
}
