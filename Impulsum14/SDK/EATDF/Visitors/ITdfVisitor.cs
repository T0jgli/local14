using EATDF.Members;
using EATDF.Types;

namespace EATDF.Visitors;

public interface ITdfVisitor
{
    /// <summary>
    /// Main method for visiting Tdf objects and their children
    /// </summary>
    /// <param name="value">The tdf object</param>
    void VisitTdf(Tdf value);
    bool VisitBlob(TdfBlob value, Tdf parent, bool visitHeader);
    bool VisitBool(TdfBool value, Tdf parent, bool visitHeader);
    bool VisitFloat(TdfFloat value, Tdf parent, bool visitHeader);
    bool VisitInt8(TdfInt8 value, Tdf parent, bool visitHeader);
    bool VisitInt16(TdfInt16 value, Tdf parent, bool visitHeader);
    bool VisitInt32(TdfInt32 value, Tdf parent, bool visitHeader);
    bool VisitInt64(TdfInt64 value, Tdf parent, bool visitHeader);
    bool VisitList<T>(TdfList<T> value, Tdf parent, bool visitHeader);
    bool VisitMap<TKey, TValue>(TdfMap<TKey, TValue> value, Tdf parent, bool visitHeader) where TKey : notnull;
    bool VisitString(TdfString value, Tdf parent, bool visitHeader);
    bool VisitStruct<TStruct>(TdfStruct<TStruct> value, Tdf parent, bool visitHeader) where TStruct : Tdf?, new();
    bool VisitUInt8(TdfUInt8 value, Tdf parent, bool visitHeader);
    bool VisitUInt16(TdfUInt16 value, Tdf parent, bool visitHeader);
    bool VisitUInt32(TdfUInt32 value, Tdf parent, bool visitHeader);
    bool VisitUInt64(TdfUInt64 value, Tdf parent, bool visitHeader);
    bool VisitEnum<TEnum>(TdfEnum<TEnum> value, Tdf parent, bool visitHeader) where TEnum : Enum, new();
    bool VisitVariable(TdfVariable value, Tdf parent, bool visitHeader);
    bool VisitBlazeObjectType(TdfObjectType value, Tdf parent, bool visitHeader);
    bool VisitBlazeObjectId(TdfObjectId value, Tdf parent, bool visitHeader);
    bool VisitTimeValue(TdfTimeValue value, Tdf parent, bool visitHeader);
    bool VisitUnion<TUnion>(TdfUnion<TUnion> value, Tdf parent, bool visitHeader) where TUnion : Union, new();
}
