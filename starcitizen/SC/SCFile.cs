using System;

namespace starcitizen.SC;

/// <summary>
/// Represents a cached Star Citizen asset file.
/// </summary>
[Serializable]
class SCFile
{
    /// <summary>
    /// Type of cached file.
    /// </summary>
    public enum FileType
    {
        UnknownFile = -1,
        PakFile = 0,
        DefProfile = 1,
        LangFile = 3,
    }

    /// <summary>Gets or sets the file type.</summary>
    public FileType Filetype { get; set; } = FileType.UnknownFile;
    
    /// <summary>Gets or sets the file name.</summary>
    public string Filename { get; set; } = "";
    
    /// <summary>Gets or sets the file path.</summary>
    public string Filepath { get; set; } = "";
    
    /// <summary>Gets or sets the file's last modified date.</summary>
    public DateTime FileDateTime { get; set; } = DateTime.UnixEpoch;
    
    /// <summary>Gets or sets the file content.</summary>
    public string Filedata { get; set; } = "";
}
