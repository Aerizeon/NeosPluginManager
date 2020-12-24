using HarmonyLib;
using FrooxEngine;
using FrooxEngine.UIX;

namespace NeosPluginManager.Patches
{
    [HarmonyPatch(typeof(FriendsDialog), "AddFriendItem")]
    public static class PatchFriendsDialog
    {
        /// <summary>
        /// patch neos's friends list to not require lock into press
        /// </summary>
        /// 
        static void Postfix(ref FriendItem __result)
        {
            Button button = __result.Slot.GetComponentInChildren<Button>();
            button.RequireLockInToPress.Value = true;
        }
    }
}
