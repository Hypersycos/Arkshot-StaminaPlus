using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace StaminaPlus
{
    [BepInPlugin("hypersycos.plugins.arkshot.staminaplus", "Stamina Plus", "1.0.0")]
    [BepInProcess("Arkshot.exe")]
    public class Plugin : BaseUnityPlugin
    {
        private static ConfigEntry<float> maxStamina;
        private static ConfigEntry<float> staminaRegenRate;
        private static ConfigEntry<float> staminaStillRate;
        private static ConfigEntry<float> staminaCrouchRate;
        private static ConfigEntry<float> staminaRegenIncreaseRate;
        private static ConfigEntry<float> staminaEmptyRegenDelay;

        private static ConfigEntry<float> dashCost;
        private static ConfigEntry<float> jumpCost;
        private static ConfigEntry<float> airJumpCost;
        private static ConfigEntry<float> sprintCost;
        private static ConfigEntry<bool> sprintDisablesRegen;

        private static ConfigEntry<float> drawCost;
        private static ConfigEntry<float> holdCost;

        private static ConfigEntry<float> coffeeRegenRate;

        public static Plugin Instance;

        private void Awake()
        {
            maxStamina = Config.Bind("General",
                "MaxStamina",
                100f,
                "Max stamina value");
            staminaRegenRate = Config.Bind("General",
                "StaminaRegenRate",
                1.5f,
                "The base stamina regeneration rate");
            staminaStillRate = Config.Bind("General",
                "StaminaStillRate",
                1.5f,
                "The extra base stamina regeneration rate when standing still");
            staminaCrouchRate = Config.Bind("General",
                "StaminaCrouchRate",
                1.5f,
                "The extra base stamina regeneration rate when crouching");
            staminaRegenIncreaseRate = Config.Bind("General",
                "StaminaRegenIncreaseRate",
                2f,
                "The multiplier per second which is added to the stamina regen rate. E.g. with a regen rate of 1.5 and a regen increase rate of 2, 3 seconds of increasing stamina results in a regen rate of 1.5 * (1 + 2 * 3) = 10.5");
            staminaEmptyRegenDelay = Config.Bind("General",
                "StaminaEmptyRegenDelay",
                1f,
                "How long stamina regen is disabled for if stamina bottoms out");
            staminaEmptyRegenDelay = Config.Bind("General",
                "StaminaEmptyRegenDelay",
                1f,
                "How long stamina regen is disabled for if stamina bottoms out");
            staminaEmptyRegenDelay = Config.Bind("General",
                "StaminaEmptyRegenDelay",
                1f,
                "How long stamina regen is disabled for if stamina bottoms out");

            dashCost = Config.Bind("Movement",
                "DashCost",
                40f,
                "The stamina cost of dashing");
            jumpCost = Config.Bind("Movement",
                "JumpCost",
                5f,
                "The cost of a grounded jump");
            airJumpCost = Config.Bind("Movement",
                "AirJumpCost",
                2.5f,
                "The increase in stamina cost per air jump");
            sprintCost = Config.Bind("Movement",
                "SprintCost",
                10f,
                "The cost per second for sprinting");
            sprintDisablesRegen = Config.Bind("Movement",
                "SprintDisablesRegen",
                true,
                "Whether sprinting completely disables stamina regen (true) or just resets the regen increase rate");

            drawCost = Config.Bind("Shooting",
                "DrawCost",
                9f,
                "The initial cost per second for drawing an arrow. Negative values can delay the hold cost.");
            holdCost = Config.Bind("Shooting",
                "HoldCost",
                9f,
                "The cost increase per second for drawing an arrow");

            coffeeRegenRate = Config.Bind("Powerups",
                "CoffeeRegenRate",
                3f,
                "The extra base stamina regen rate given by coffee.");

            // Plugin startup logic
            Instance = this;
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
            Harmony.CreateAndPatchAll(typeof(Patches));
        }

        private static class Helpers
        {
            public static CodeInstruction GetNext(IEnumerator<CodeInstruction> enumerator)
            {
                if (enumerator.MoveNext())
                {
                    return enumerator.Current;
                }
                else
                {
                    return null;
                }
            }

            public static IEnumerable<CodeInstruction> LoopWhile(IEnumerator<CodeInstruction> enumerator, Func<CodeInstruction, bool> action)
            {
                CodeInstruction instr = GetNext(enumerator);
                while (action(instr))
                {
                    yield return instr;
                    instr = GetNext(enumerator);
                }
                if (instr != null)
                {
                    yield return instr;
                }
            }

            public static IEnumerable<CodeInstruction> ModifyFirst(IEnumerator<CodeInstruction> enumerator, Func<CodeInstruction, bool> cond, Action<CodeInstruction> action)
            {
                CodeInstruction instr = GetNext(enumerator);
                while (cond(instr))
                {
                    yield return instr;
                    instr = GetNext(enumerator);
                }
                action(instr);
                yield return instr;
            }

            public static void SkipX(IEnumerator<CodeInstruction> enumerator, int x)
            {
                for (int i = 0; i < x; i++)
                {
                    enumerator.MoveNext();
                }
            }

            public static IEnumerable<CodeInstruction> PassX(IEnumerator<CodeInstruction> enumerator, int x)
            {
                for (int i = 0; i < x; i++)
                {
                    yield return GetNext(enumerator);
                }
            }

            public static IEnumerable<CodeInstruction> CondSkip(IEnumerator<CodeInstruction> enumerator, int x, bool skip)
            {
                if (skip)
                {
                    SkipX(enumerator, x);
                    yield break;
                }
                else
                {
                    foreach (CodeInstruction instr in PassX(enumerator, x))
                    {
                        yield return instr;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(controls))]
        public class Patches
        {
            [HarmonyPatch("Awake")]
            [HarmonyPostfix]
            static void constructor_Postfix(ref float ___stamina)
            {
                ___stamina = maxStamina.Value;
            }

            [HarmonyTranspiler]
            [HarmonyPatch("StaminaLogic")]
            static IEnumerable<CodeInstruction> staminaLogic_Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                IEnumerator<CodeInstruction> enumerator = instructions.GetEnumerator();
                Func<CodeInstruction, bool> NextStamina = instr => instr.opcode != OpCodes.Ldfld || instr.operand.ToString() != "System.Single stamina";
                
                CodeInstruction ldthis = null;

                //bind helpers to enumerator
                Func<CodeInstruction> GetNext = () => Helpers.GetNext(enumerator);
                Func<Func<CodeInstruction, bool>, IEnumerable<CodeInstruction>> LoopWhile = (action) => Helpers.LoopWhile(enumerator, action);
                Func<Func<CodeInstruction, bool>, Action<CodeInstruction>, IEnumerable<CodeInstruction>> ModifyFirst = (cond, action) => Helpers.ModifyFirst(enumerator, cond, action);
                Action<int> SkipX = (x) => Helpers.SkipX(enumerator, x);
                Func<int, IEnumerable<CodeInstruction>> PassX = (x) => Helpers.PassX(enumerator, x);
                Func<int, bool, IEnumerable<CodeInstruction>> CondSkip = (x, skip) => Helpers.CondSkip(enumerator, x, skip);

                Func<CodeInstruction, float, bool> NextF = (instr, val) => instr.opcode != OpCodes.Ldc_R4 || (float)instr.operand != val;

                Instance.Logger.LogInfo("Patching base regen");
                //BASE REGEN
                foreach (CodeInstruction instr in ModifyFirst(instr => NextF(instr, 1.5f), instr => instr.operand = staminaRegenRate.Value))
                {
                    yield return instr;
                }

                Instance.Logger.LogInfo("Patching stationary regen");
                //STATIONARY REGEN
                foreach (CodeInstruction instr in ModifyFirst(instr => NextF(instr, 1.5f), instr => instr.operand = staminaStillRate.Value))
                {
                    yield return instr;
                }

                Instance.Logger.LogInfo("Patching crouching regen");
                //CROUCHING REGEN
                foreach (CodeInstruction instr in ModifyFirst(instr => NextF(instr, 1.5f), instr => instr.operand = staminaCrouchRate.Value))
                {
                    yield return instr;
                }

                Instance.Logger.LogInfo("Patching coffee regen");
                //COFFEE REGEN
                foreach (CodeInstruction instr in ModifyFirst(instr => NextF(instr, 3f), instr => instr.operand = coffeeRegenRate.Value))
                {
                    yield return instr;
                }

                Instance.Logger.LogInfo("Patching max stamina");
                //MAX STAMINA
                foreach (CodeInstruction instr in ModifyFirst(instr => NextF(instr, 100f), instr => instr.operand = maxStamina.Value))
                { // if stamina > max
                    yield return instr;
                }
                foreach (CodeInstruction instr in ModifyFirst(instr => NextF(instr, 100f), instr => instr.operand = maxStamina.Value))
                { // set stamina to max
                    yield return instr;
                }

                Instance.Logger.LogInfo("Patching stamina regen delay");
                //STAMINA REGEN DELAY
                foreach (CodeInstruction instr in ModifyFirst(instr => NextF(instr, 1f), instr => instr.operand = staminaEmptyRegenDelay.Value))
                {
                    yield return instr;
                }

                //STAMINA BAR
                foreach (CodeInstruction instr in ModifyFirst(instr => NextF(instr, 5f), instr => instr.operand = 5f * 100f / maxStamina.Value))
                {
                    yield return instr;
                }

                Instance.Logger.LogInfo("Patching stamina regen increase");
                //STAMINA REGEN RATE INCREASE
                foreach (CodeInstruction instr in ModifyFirst(instr => NextF(instr, 2f), instr => instr.operand = staminaRegenIncreaseRate.Value))
                {
                    yield return instr;
                }

                //don't touch rest
                foreach (CodeInstruction instr in LoopWhile(instr => instr != null))
                {
                    yield return instr;
                }
            }

            [HarmonyTranspiler]
            [HarmonyPatch("move")]
            static IEnumerable<CodeInstruction> move_Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                IEnumerator<CodeInstruction> enumerator = instructions.GetEnumerator();
                Func<CodeInstruction, bool> NextStamina = instr => instr.opcode != OpCodes.Ldfld || instr.operand.ToString() != "System.Single stamina";
                CodeInstruction ldthis = null;
                CodeInstruction ldc0 = null;

                //bind helpers to enumerator
                Func<CodeInstruction> GetNext = () => Helpers.GetNext(enumerator);
                Func<Func<CodeInstruction, bool>, IEnumerable<CodeInstruction>> LoopWhile = (action) => Helpers.LoopWhile(enumerator, action);
                Action<int> SkipX = (x) => Helpers.SkipX(enumerator, x);
                Func<int, IEnumerable<CodeInstruction>> PassX = (x) => Helpers.PassX(enumerator, x);
                Func<int, bool, IEnumerable<CodeInstruction>> CondSkip = (x, skip) => Helpers.CondSkip(enumerator, x, skip);

                Instance.Logger.LogInfo("Patching dash");
                //DASH
                foreach (CodeInstruction instr in LoopWhile(instr => instr.opcode != OpCodes.Ldstr || instr.operand.ToString() != "Pulldash"))
                {
                    yield return instr;
                }
                foreach (CodeInstruction instr in PassX(2))
                {
                    yield return instr;
                }
                foreach (CodeInstruction instr in CondSkip(4, dashCost.Value == 0))
                { //only check stamina < 0 if dash has a stamina cost
                    yield return instr;
                }

                CodeInstruction last = null;
                foreach (CodeInstruction instr in LoopWhile(NextStamina))
                {
                    yield return instr;
                    last = instr;
                }
                CodeInstruction temp = GetNext().Clone();
                temp.operand = dashCost.Value;
                yield return temp;
                yield return GetNext();
                yield return GetNext();
                foreach (CodeInstruction instr in CondSkip(3, dashCost.Value == 0))
                { //only set stamina regen to 1 if dash has a stamina cost
                    yield return instr;
                }

                Instance.Logger.LogInfo("Patching jump");
                //JUMP
                foreach (CodeInstruction instr in LoopWhile(i => i.opcode != OpCodes.Ldstr || i.operand.ToString() != "Gamepad jump"))
                {
                    yield return instr;
                }
                foreach (CodeInstruction instr in PassX(3))
                {
                    yield return instr;
                }
                CodeInstruction skipJumpIf = null;
                if (jumpCost.Value == 0)
                {
                    if (airJumpCost.Value > 0)
                    { //only check stamina if not grounded
                        ldthis = GetNext();
                        CodeInstruction ldstamina = GetNext();
                        ldc0 = GetNext();
                        skipJumpIf = GetNext();
                        foreach (CodeInstruction instr in PassX(3))
                        {
                            yield return instr;
                        }
                        yield return ldthis;
                        yield return ldstamina;
                        yield return ldc0;
                        yield return skipJumpIf;
                    }
                    else
                    { //don't check stamina at all
                        SkipX(4);
                    }
                }
                else
                { //skip over this stamina reference
                    foreach (CodeInstruction instr in PassX(2))
                    {
                        yield return instr;
                    }
                }
                foreach (CodeInstruction instr in LoopWhile(NextStamina))
                {
                    yield return instr;
                }
                temp = GetNext().Clone();
                temp.operand = jumpCost.Value;
                yield return temp;
                temp = temp.Clone();
                temp.operand = airJumpCost.Value;
                yield return temp;
                GetNext();
                if (jumpCost.Value == 0)
                {
                    yield return GetNext();
                    CodeInstruction loadAirJumps = GetNext();
                    yield return loadAirJumps;
                    CodeInstruction toFloat = GetNext();
                    yield return toFloat;
                    foreach (CodeInstruction instr in LoopWhile(i => i.opcode != OpCodes.Stfld || i.operand.ToString() != "System.Single stamina"))
                    {
                        yield return instr;
                    }
                    if (airJumpCost.Value > 0)
                    { //don't reset stamina regen if air jumps <= 0
                        yield return ldthis.Clone();
                        yield return loadAirJumps.Clone();
                        yield return toFloat.Clone();
                        yield return ldc0.Clone();
                        yield return skipJumpIf.Clone();
                    }
                    else
                    { //don't reset stamina
                        SkipX(3);
                    }
                }

                Instance.Logger.LogInfo("Patching sprint");
                //SPRINTING
                foreach (CodeInstruction instr in LoopWhile(instr => instr.opcode != OpCodes.Ldstr || instr.operand.ToString() != "Sprint"))
                {
                    yield return instr;
                }
                foreach (CodeInstruction instr in PassX(2))
                {
                    yield return instr;
                }
                foreach (CodeInstruction instr in CondSkip(4, sprintCost.Value == 0))
                { //only check stamina < 0 if sprinting has a stamina cost
                    yield return instr;
                }
                foreach (CodeInstruction instr in LoopWhile(NextStamina))
                {
                    yield return instr;
                }
                yield return GetNext();
                temp = GetNext();
                temp.operand = sprintCost.Value;
                yield return temp;
                yield return GetNext();
                yield return GetNext();
                yield return GetNext();
                if (sprintCost.Value == 0)
                {
                    SkipX(3);
                }
                else
                {
                    yield return GetNext();
                    temp = GetNext();
                    if (!sprintDisablesRegen.Value)
                    {
                        temp.operand = 1f;
                    }
                    yield return temp;
                }

                //don't touch rest
                foreach(CodeInstruction instr in LoopWhile(instr => instr != null))
                {
                    yield return instr;
                }
            }

            [HarmonyTranspiler]
            [HarmonyPatch("shoot")]
            static IEnumerable<CodeInstruction> shoot_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                IEnumerator<CodeInstruction> enumerator = instructions.GetEnumerator();
                CodeInstruction ldthis = null;

                Func<CodeInstruction> GetNext = () => Helpers.GetNext(enumerator);
                Func<Func<CodeInstruction, bool>, IEnumerable<CodeInstruction>> LoopWhile = (action) => Helpers.LoopWhile(enumerator, action);
                Action<int> SkipX = (x) => Helpers.SkipX(enumerator, x);
                Func<int, IEnumerable<CodeInstruction>> PassX = (x) => Helpers.PassX(enumerator, x);
                Func<int, bool, IEnumerable<CodeInstruction>> CondSkip = (x, skip) => Helpers.CondSkip(enumerator, x, skip);

                Instance.Logger.LogInfo("Patching fire");
                //INITIAL FIRE
                foreach (CodeInstruction instr in LoopWhile(instr => instr.opcode != OpCodes.Ldstr || instr.operand.ToString() != "Gamepad fire"))
                {
                    yield return instr;
                }
                foreach (CodeInstruction instr in PassX(3))
                {
                    yield return instr;
                }
                //only check stamina < 0 if initial draw has a stamina cost
                if (drawCost.Value <= 0)
                { //need to preserve label for earlier branch
                    ldthis = GetNext();
                    SkipX(4);
                    yield return ldthis;
                }
                else
                {
                    foreach (CodeInstruction instr in PassX(4))
                    {
                        yield return instr;
                    }
                    ldthis = GetNext();
                    yield return ldthis;
                }

                Instance.Logger.LogInfo("Patching hold");
                //HOLDING
                foreach (CodeInstruction instr in LoopWhile(instr => instr.opcode != OpCodes.Ldfld || instr.operand.ToString() != "System.Single slowDownModifier"))
                {
                    yield return instr;
                }
                foreach (CodeInstruction instr in PassX(2))
                {
                    yield return instr;
                }

                LocalBuilder drain = generator.DeclareLocal(typeof(float));
                CodeInstruction loadHoldTime = null;
                List<CodeInstruction> toAppend = new();

                //store new drain in local variable
                yield return new CodeInstruction(OpCodes.Ldc_R4, 0f);
                yield return ldthis.Clone();
                // <load field holdtime, grabbed later>
                toAppend.Add(new CodeInstruction(OpCodes.Ldc_R4, holdCost.Value / 3f));
                toAppend.Add(new CodeInstruction(OpCodes.Mul));
                toAppend.Add(new CodeInstruction(OpCodes.Ldc_R4, drawCost.Value));
                toAppend.Add(new CodeInstruction(OpCodes.Add));
                toAppend.Add(CodeInstruction.Call(typeof(UnityEngine.Mathf), "Max", parameters: new[] { typeof(float), typeof(float)}));
                toAppend.Add(new CodeInstruction(OpCodes.Stloc, drain.LocalIndex));

                foreach (CodeInstruction instr in PassX(7))
                {
                    toAppend.Add(instr);
                }

                
                //replace stamina > 0 with (drain <= 0 || stamina > 0)

                List<Label> startCheckLabels = GetNext().labels;
                CodeInstruction ldStamina = GetNext();
                CodeInstruction ld0float = GetNext();
                CodeInstruction skipIf = GetNext();
                CodeInstruction otherConds = GetNext();
                Label newLabel = generator.DefineLabel();
                otherConds.labels.Add(newLabel);

                //drain <= 0
                CodeInstruction startCheck = new CodeInstruction(OpCodes.Ldloc, drain.LocalIndex);
                startCheck.labels.AddRange(startCheckLabels);
                toAppend.Add(startCheck);
                toAppend.Add(ld0float.Clone());
                toAppend.Add(new CodeInstruction(OpCodes.Ble, newLabel));

                //stamina > 0
                toAppend.Add(ldthis.Clone());
                toAppend.Add(ldStamina);
                toAppend.Add(ld0float);
                toAppend.Add(skipIf);

                toAppend.Add(otherConds);
                foreach (CodeInstruction instr in LoopWhile(instr => instr.opcode != OpCodes.Stfld || instr.operand.ToString() != "System.Single holdtime"))
                {
                    toAppend.Add(instr);
                }

                
                //reduce stamina by drain * deltatime, only if drain > 0

                //drain > 0?
                toAppend.Add(new CodeInstruction(OpCodes.Ldloc, drain.LocalIndex));
                toAppend.Add(ld0float.Clone());
                Label endTakeStamina = generator.DefineLabel();
                toAppend.Add(new CodeInstruction(OpCodes.Ble_Un, endTakeStamina));
                
                //load stamina
                foreach(CodeInstruction instr in PassX(4))
                {
                    toAppend.Add(instr);
                }
                //use drain instead of original calculation
                toAppend.Add(new CodeInstruction(OpCodes.Ldloc, drain.LocalIndex));
                SkipX(1);
                loadHoldTime = GetNext();
                SkipX(2);

                //No more dependencies, so can finally yield all stored instructions
                yield return loadHoldTime.Clone();
                foreach(CodeInstruction instr in toAppend)
                {
                    yield return instr;
                }

                //Navigate to end of stamina if branch
                foreach (CodeInstruction instr in PassX(6))
                {
                    yield return instr;
                }

                //add label for branch
                CodeInstruction temp = GetNext();
                temp.labels.Add(endTakeStamina);
                yield return temp;

                //don't touch rest
                foreach (CodeInstruction instr in LoopWhile(instr => instr != null))
                {
                    yield return instr;
                }
            }
        }
    }
}
