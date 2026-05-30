using System.Collections.Generic;

public static class SimulationInputGate
{
    private static readonly HashSet<object> LockOwners = new();

    public static bool IsLocked => LockOwners.Count > 0;

    public static void Lock(object owner)
    {
        if (owner == null)
            return;

        LockOwners.Add(owner);
    }

    public static void Unlock(object owner)
    {
        if (owner == null)
            return;

        LockOwners.Remove(owner);
    }
}
