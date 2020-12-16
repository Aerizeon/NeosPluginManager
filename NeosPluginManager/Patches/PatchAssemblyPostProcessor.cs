using HarmonyLib;
using System.Reflection.Emit;
using PostX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BaseX;

namespace NeosPluginManager.Patches
{
    [HarmonyPatch(typeof(NeosAssemblyPostProcessor), "Process")]
    public static class PatchAssemblyPostProcessor
    {
        /// <summary>
        /// Patches Neos' Assembly PostProcessor so that it doesn't check for the POSTX_PROCESSED attribute
        /// This should be improved so that it's more reliable if the code is changed.
        /// </summary>
        /// <param name="instructions"></param>
        /// <returns></returns>
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var opcodes = new List<CodeInstruction>(instructions);
            opcodes.RemoveRange(190, 44);
            return opcodes.AsEnumerable();
        }
    }
}
