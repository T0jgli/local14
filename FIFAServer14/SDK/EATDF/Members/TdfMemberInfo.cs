using System;
using System.Text;

namespace EATDF.Members;

public class TdfMemberInfo
{
    /// <summary>
    /// The name of C# class member that this info is associated with.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The original TDF member name (usually starts with 'm').
    /// </summary>
    public string TdfMemberName { get; }

    /// <summary>
    /// Tag string representation (uppercase).
    /// </summary>
    public string TagString { get; }

    /// <summary>
    /// TDF Tag.
    /// </summary>
    public uint Tag { get; }

    /// <summary>
    /// TDF Type.
    /// </summary>
    public TdfType Type { get; }

    /// <summary>
    /// The index of the member in the TDF structure. Indexes are determined by sorting by the tag (ascending).
    /// </summary>
    public int MemberIndex { get; }

    /// <summary>
    /// Returns true if the tag is used only once in the TDF structure.
    /// </summary>
    public bool IsUnique { get; }

    public TdfMemberInfo(string name, string tdfMemberName, uint tag, TdfType type, int memberIndex, bool isUnique)
    {
        Name = name;
        TdfMemberName = tdfMemberName;
        TagString = Helpers.TagToString(tag);
        Tag = tag;
        Type = type;
        MemberIndex = memberIndex;
        IsUnique = isUnique;
    }

    public TdfMemberInfo(uint tag, TdfType type)
    {
        TagString = Helpers.TagToString(tag);
        Name = TagString;
        TdfMemberName = TagString;
        Tag = tag;
        Type = type;
        MemberIndex = -1;
        IsUnique = true;
    }


}
