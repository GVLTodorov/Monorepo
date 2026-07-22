using System;

namespace Sqlite.Helpers;

/// <summary>
/// Identifies a field that contains sensitive data (PII) and may be obfuscated when logging or persisting
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class SensitiveDataAttribute : Attribute
{
}
