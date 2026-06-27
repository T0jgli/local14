using EATDF.Visitors;

namespace EATDF.Members;

public class TdfInt8 : TdfMember<sbyte>
{
    public TdfInt8(TdfMemberInfo typeInfo) : base(typeInfo)
    {
    }

    public override sbyte DefaultValue()
    {
        return 0;
    }

    public override sbyte InitValue()
    {
        return 0;
    }

    public override bool IsSet()
    {
        return UserSet;
    }

    public override bool Visit(ITdfVisitor visitor, Tdf parent, bool visitHeader)
    {
        return visitor.VisitInt8(this, parent, visitHeader);
    }

    public override string ToString()
    {
        return $"{Value} (0x{Value:X2})";
    }
}
