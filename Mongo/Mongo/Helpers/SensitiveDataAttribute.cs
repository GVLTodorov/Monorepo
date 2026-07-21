using System;

namespace Mongo.Helpers;

/// <summary>
/// Identifies a field that contains sensitive data (PII data) and forces it to be obfuscated when saving
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class SensitiveDataAttribute : Attribute
{
}