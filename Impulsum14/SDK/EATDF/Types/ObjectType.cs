
namespace EATDF.Types;

public struct ObjectType : IComparable<ObjectType>
{
    public ushort Component { get; set; }
    public ushort Type { get; set; }

    public ObjectType()
    {
        Component = 0;
        Type = 0;
    }

    public ObjectType(ushort component, ushort type)
    {
        Component = component;
        Type = type;
    }

    public ObjectType(uint objectType)
    {
        Type = (ushort)(objectType & 0xFFFF);
        Component = (ushort)((objectType >> 16) & 0xFFFF);
    }

    public override string ToString()
    {
        return $"{Component}/{Type}";
    }

    public uint ToUInt32()
    {
        unchecked
        {
            uint objectType = 0;
            objectType |= ((uint)Component << 16);
            objectType |= Type;
            return objectType;
        }
    }

    public int CompareTo(ObjectType other)
    {
        uint thisValue = ToUInt32();
        uint otherValue = other.ToUInt32();
        return thisValue.CompareTo(otherValue);
    }
}
