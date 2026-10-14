using UnrealBuildTool;
using System.Collections.Generic;

public class LastStanceUnrealEditorTarget : TargetRules
{
    public LastStanceUnrealEditorTarget(TargetInfo Target) : base(Target)
    {
        Type = TargetType.Editor;
        DefaultBuildSettings = BuildSettingsVersion.V6;
        IncludeOrderVersion = EngineIncludeOrderVersion.Latest;
        ExtraModuleNames.Add("LastStanceUnreal");
    }
}
