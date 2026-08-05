using EATDF.Visitors;

namespace EATDF.Members;

public class TdfUInt8 : TdfMember<byte>
{
    public TdfUInt8(TdfMemberInfo typeInfo) : base(typeInfo)
    {
    }

    public override byte DefaultValue()
    {
        return 0;
    }

    public override byte InitValue()
    {
        return 0;
    }

    public override bool IsSet()
    {
        return UserSet;
    }

    public override bool Visit(ITdfVisitor visitor, Tdf parent, bool visitHeader)
    {
        return visitor.VisitUInt8(this, parent, visitHeader);
    }

    public override string ToString()
    {
        return $"{Value} (0x{Value:X2})";
    }
}
