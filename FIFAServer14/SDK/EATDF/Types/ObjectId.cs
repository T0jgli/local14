
namespace EATDF.Types;

public struct ObjectId : IComparable<ObjectId>
{
    public ObjectType Type { get; set; }
    public long Id { get; set; }

    public ObjectId()
    {
        Type = new ObjectType();
        Id = 0;
    }

    public ObjectId(ObjectType type)
    {
        Type = type;
        Id = 0;
    }

    public ObjectId(long id)
    {
        Type = new ObjectType();
        Id = id;
    }

    public ObjectId(ObjectType type, long id)
    {
        Type = type;
        Id = id;
    }

    public ObjectId(ushort component, ushort type, long id)
    {
        Type = new ObjectType(component, type);
        Id = id;
    }

    public override string ToString()
    {
        return $"{Type}/{Id}";
    }

    public int CompareTo(ObjectId other)
    {
        int typeComparison = Type.CompareTo(other.Type);
        if (typeComparison != 0)
            return typeComparison;

        return Id.CompareTo(other.Id);
    }
}
