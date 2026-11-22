namespace Rehearsal.Data;

public enum ActionTypes
{
    ANY,
    GCD,
    OGCD
}

public static class ActionTypesExtension
{
    public static string PrettyPrint(this ActionTypes actionType)
    {
        return actionType switch
        {
            ActionTypes.GCD => "GCD",
            ActionTypes.OGCD => "oGCD",
            _ => "Any"
        };
    }
}
