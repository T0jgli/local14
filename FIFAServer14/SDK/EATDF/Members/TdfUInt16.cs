using EATDF.Visitors;

namespace EATDF.Members;

public class TdfUInt16 : TdfMember<ushort>
{
    public TdfUInt16(TdfMemberInfo typeInfo) : base(typeInfo)
    {
    }

    public override ushort DefaultValue()
    {
        return 0;
    }

    public override ushort InitValue()
    {
        return 0;
    }

    public override bool IsSet()
    {
        return UserSet;
    }

    public override bool Visit(ITdfVisitor visitor, Tdf parent, bool visitHeader)
    {
        return visitor.VisitUInt16(this, parent, visitHeader);
    }

    public override string ToString()
    {
        return $"{Value} (0x{Value:X4})";
    }
}

