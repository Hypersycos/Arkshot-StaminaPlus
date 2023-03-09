using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using MultiplayerSync;
using BepInEx.Logging;

namespace StaminaPlus
{
    [BepInPlugin("hypersycos.plugins.arkshot.staminaplus", "Stamina Plus", "2.0.0")]
    [BepInDependency("hypersycos.plugins.arkshot.multiplayersync")]
    [BepInProcess("Arkshot.exe")]
    public class Plugin : BaseUnityPlugin
    {
        private static SyncedConfigEntry<float> maxStamina = new();
        private static SyncedConfigEntry<float> staminaRegenRate = new();
        private static SyncedConfigEntry<float> staminaStillRate = new();
        private static SyncedConfigEntry<float> staminaCrouchRate = new();
        private static SyncedConfigEntry<float> staminaRegenIncreaseRate = new();
        private static SyncedConfigEntry<float> staminaEmptyRegenDelay = new();

        private static SyncedConfigEntry<float> dashCost = new();
        private static SyncedConfigEntry<float> jumpCost = new();
        private static SyncedConfigEntry<float> airJumpCost = new();
        private static SyncedConfigEntry<float> sprintCost = new();
        private static SyncedConfigEntry<bool> sprintDisablesRegen = new();

        private static SyncedConfigEntry<float> drawCost = new();
        private static SyncedConfigEntry<float> holdCost = new();

        private static SyncedConfigEntry<float> coffeeRegenRate = new();

        public static Plugin Instance;

        private static controls controlsInstance = null;

        private void Awake()
        {
            maxStamina.Bind(Config.Bind("General",
                "MaxStamina",
                100f,
                "Max stamina value"));
            staminaRegenRate.Bind(Config.Bind("General",
                "StaminaRegenRate",
                1.5f,
                "The base stamina regeneration rate"));
            staminaStillRate.Bind(Config.Bind("General",
                "StaminaStillRate",
                1.5f,
                "The extra base stamina regeneration rate when standing still"));
            staminaCrouchRate.Bind(Config.Bind("General",
                "StaminaCrouchRate",
                1.5f,
                "The extra base stamina regeneration rate when crouching"));
            staminaRegenIncreaseRate.Bind(Config.Bind("General",
                "StaminaRegenIncreaseRate",
                2f,
                "The multiplier per second which is added to the stamina regen rate. E.g. with a regen rate of 1.5 and a regen increase rate of 2, 3 seconds of increasing stamina results in a regen rate of 1.5 * (1 + 2 * 3) = 10.5"));
            staminaEmptyRegenDelay.Bind(Config.Bind("General",
                "StaminaEmptyRegenDelay",
                1f,
                "How long stamina regen is disabled for if stamina bottoms out"));

            dashCost.Bind(Config.Bind("Movement",
                "DashCost",
                40f,
                "The stamina cost of dashing. Minimum 0."));
            jumpCost.Bind(Config.Bind("Movement",
                "JumpCost",
                5f,
                "The cost of a grounded jump. Minimum 0."));
            airJumpCost.Bind(Config.Bind("Movement",
                "AirJumpCost",
                2.5f,
                "The increase in stamina cost per air jump. Minimum 0."));
            sprintCost.Bind(Config.Bind("Movement",
                "SprintCost",
                10f,
                "The cost per second for sprinting. Minimum 0."));
            sprintDisablesRegen.Bind(Config.Bind("Movement",
                "SprintDisablesRegen",
                true,
                "Whether sprinting completely disables stamina regen (true) or just resets the regen increase rate"));

            drawCost.Bind(Config.Bind("Shooting",
                "DrawCost",
                3f,
                "The initial cost per second for drawing an arrow. Negative values can delay the hold cost."));
            holdCost.Bind(Config.Bind("Shooting",
                "HoldCost",
                9f,
                "The cost increase per second for drawing an arrow"));

            coffeeRegenRate.Bind(Config.Bind("Powerups",
                "CoffeeRegenRate",
                3f,
                "The extra base stamina regen rate given by coffee."));

            if (dashCost.Value < 0)
            {
                dashCost.Value = 0;
            }
            if (jumpCost.Value < 0)
            {
                jumpCost.Value = 0;
            }
            if (airJumpCost.Value < 0)
            {
                airJumpCost.Value = 0;
            }
            if (sprintCost.Value < 0)
            {
                sprintCost.Value = 0;
            }

            MultiplayerSync.Plugin.OnJoin += Helpers.SetStaminaToMax;
            // Plugin startup logic
            Instance = this;
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
            Harmony.CreateAndPatchAll(typeof(Patches));
        }

        private static class Helpers
        {
            public static void SetStaminaToMax()
            {
                if (controlsInstance == null)
                {
                    return;
                }
                var stamina = controlsInstance.GetType().GetField("stamina", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                stamina.SetValue(controlsInstance, maxStamina.Value);
            }
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

            public static IEnumerable<CodeInstruction> SkipCond(IEnumerator<CodeInstruction> enumerator, Func<CodeInstruction, bool> cond)
            {
                CodeInstruction instr = GetNext(enumerator);
                while (!cond(instr))
                {
                    yield return instr;
                    instr = GetNext(enumerator);
                }
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
            static void constructor_Postfix(ref float ___stamina, controls __instance, ref PhotonView ___myView)
            {
                if (___myView.isMine)
                {
                    ___stamina = maxStamina.Value;
                    if (!PhotonNetwork.isMasterClient)
                    {
                        controlsInstance = __instance;
                    }
                }
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
                Func<Func<CodeInstruction, bool>, IEnumerable<CodeInstruction>> SkipCond = (cond) => Helpers.SkipCond(enumerator, cond);
                Action<int> SkipX = (x) => Helpers.SkipX(enumerator, x);
                Func<int, IEnumerable<CodeInstruction>> PassX = (x) => Helpers.PassX(enumerator, x);
                Func<int, bool, IEnumerable<CodeInstruction>> CondSkip = (x, skip) => Helpers.CondSkip(enumerator, x, skip);

                Func<CodeInstruction, float, bool> NextF = (instr, val) => instr.opcode == OpCodes.Ldc_R4 && (float)instr.operand == val;

                Instance.Logger.LogInfo("Patching base regen");
                //BASE REGEN
                foreach (CodeInstruction instr in SkipCond(instr => NextF(instr, 1.5f)))
                {
                    yield return instr;
                }
                foreach (CodeInstruction instr in staminaRegenRate.SyncedEntry.GetValueIL())
                {
                    yield return instr;
                }

                Instance.Logger.LogInfo("Patching stationary regen");
                //STATIONARY REGEN
                foreach (CodeInstruction instr in SkipCond(instr => NextF(instr, 1.5f)))
                {
                    yield return instr;
                }
                foreach (CodeInstruction instr in staminaStillRate.SyncedEntry.GetValueIL())
                {
                    yield return instr;
                }

                Instance.Logger.LogInfo("Patching crouching regen");
                //CROUCHING REGEN
                foreach (CodeInstruction instr in SkipCond(instr => NextF(instr, 1.5f)))
                {
                    yield return instr;
                }
                foreach (CodeInstruction instr in staminaCrouchRate.SyncedEntry.GetValueIL())
                {
                    yield return instr;
                }

                Instance.Logger.LogInfo("Patching coffee regen");
                //COFFEE REGEN
                foreach (CodeInstruction instr in SkipCond(instr => NextF(instr, 3f)))
                {
                    yield return instr;
                }
                foreach (CodeInstruction instr in coffeeRegenRate.SyncedEntry.GetValueIL())
                {
                    yield return instr;
                }

                Instance.Logger.LogInfo("Patching max stamina");
                //MAX STAMINA
                foreach (CodeInstruction instr in SkipCond(instr => NextF(instr, 100f)))
                {
                    yield return instr;
                }
                foreach (CodeInstruction instr in maxStamina.SyncedEntry.GetValueIL())
                {
                    yield return instr;
                }

                foreach (CodeInstruction instr in SkipCond(instr => NextF(instr, 100f)))
                {
                    yield return instr;
                }
                foreach (CodeInstruction instr in maxStamina.SyncedEntry.GetValueIL())
                {
                    yield return instr;
                }

                Instance.Logger.LogInfo("Patching stamina regen delay");
                //STAMINA REGEN DELAY
                foreach (CodeInstruction instr in SkipCond(instr => NextF(instr, 1f)))
                {
                    yield return instr;
                }
                foreach (CodeInstruction instr in staminaEmptyRegenDelay.SyncedEntry.GetValueIL())
                {
                    yield return instr;
                }

                //STAMINA BAR
                foreach (CodeInstruction instr in SkipCond(instr => NextF(instr, 5f)))
                {
                    yield return instr;
                }
                yield return new CodeInstruction(OpCodes.Ldc_R4, 500f);
                foreach (CodeInstruction instr in maxStamina.SyncedEntry.GetValueIL())
                {
                    yield return instr;
                }
                yield return new CodeInstruction(OpCodes.Div);

                Instance.Logger.LogInfo("Patching stamina regen increase");
                //STAMINA REGEN RATE INCREASE
                foreach (CodeInstruction instr in SkipCond(instr => NextF(instr, 2f)))
                {
                    yield return instr;
                }
                foreach (CodeInstruction instr in staminaRegenIncreaseRate.SyncedEntry.GetValueIL())
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
            static IEnumerable<CodeInstruction> move_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
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

                //only check stamina >= 0 if dash has a stamina cost
                Label skipStaminaCheck = generator.DefineLabel();
                foreach (CodeInstruction instr in dashCost.SyncedEntry.GetValueIL())
                {
                    yield return instr;
                }
                yield return new CodeInstruction(OpCodes.Ldc_R4, 0f);
                yield return new CodeInstruction(OpCodes.Ble, skipStaminaCheck);
                foreach (CodeInstruction instr in PassX(4))
                {
                    yield return instr;
                }

                CodeInstruction last = GetNext();
                last.labels.Add(skipStaminaCheck);
                yield return last;

                //get cost from synced value
                foreach (CodeInstruction instr in LoopWhile(NextStamina))
                {
                    yield return instr;
                    last = instr;
                }
                SkipX(1);
                foreach (CodeInstruction instr in dashCost.SyncedEntry.GetValueIL())
                {
                    yield return instr;
                }
                yield return GetNext();
                yield return GetNext();

                //only set stamina regen to 1 if dash has a stamina cost
                skipStaminaCheck = generator.DefineLabel();
                foreach (CodeInstruction instr in dashCost.SyncedEntry.GetValueIL())
                {
                    yield return instr;
                }
                yield return new CodeInstruction(OpCodes.Ldc_R4, 0f);
                yield return new CodeInstruction(OpCodes.Beq, skipStaminaCheck);

                foreach (CodeInstruction instr in PassX(3))
                {
                    yield return instr;
                }
                last = GetNext();
                last.labels.Add(skipStaminaCheck);
                yield return last;

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

                //replace stamina check with ((jumpCost <= 0 && grounded) || (jumpCost <= 0 && airJumpCost <= 0) || stamina > 0);
                ldthis = GetNext();
                CodeInstruction ldstamina = GetNext();
                ldc0 = GetNext();
                CodeInstruction skipJumpIf = GetNext();

                // jumpCost <= 0f
                bool done = false;
                foreach (CodeInstruction instr in jumpCost.SyncedEntry.GetValueIL())
                {
                    if (!done)
                    { //copy label from stamina > 0, used by if jump input branch
                        instr.labels.AddRange(ldthis.labels);
                        done = true;
                    }
                    yield return instr;
                }
                yield return ldc0.Clone();
                Label nextCheck = generator.DefineLabel();
                yield return new CodeInstruction(OpCodes.Bgt, nextCheck);

                // && this.grounded
                yield return ldthis.Clone();
                yield return CodeInstruction.LoadField(typeof(controls), "grounded");
                Label endCheck = generator.DefineLabel();
                yield return new CodeInstruction(OpCodes.Brtrue, endCheck);

                // || jumpCost <= 0f
                done = false;
                foreach (CodeInstruction instr in jumpCost.SyncedEntry.GetValueIL())
                {
                    if (!done)
                    {
                        instr.labels.Add(nextCheck);
                        done = true;
                    }
                    yield return instr;
                }
                yield return ldc0.Clone();
                nextCheck = generator.DefineLabel();
                yield return new CodeInstruction(OpCodes.Bgt, nextCheck);

                // && airJumpCost <= 0f
                foreach (CodeInstruction instr in airJumpCost.SyncedEntry.GetValueIL())
                {
                    yield return instr;
                }
                yield return ldc0.Clone();
                yield return new CodeInstruction(OpCodes.Ble, endCheck);

                // || stamina > 0f
                // clear original label, add our new one
                ldthis.labels = new() {nextCheck};
                yield return ldthis;
                yield return ldstamina;
                yield return ldc0;
                yield return skipJumpIf;

                last = GetNext();
                last.labels.Add(endCheck);
                yield return last;

                foreach (CodeInstruction instr in LoopWhile(instr => !instr.StoresField(AccessTools.Field(typeof(controls), nameof(controls.airJumps)))))
                {
                    yield return instr;
                }
                foreach (CodeInstruction instr in LoopWhile(instr => !instr.StoresField(AccessTools.Field(typeof(controls), nameof(controls.airJumps)))))
                {
                    yield return instr;
                }

                //store jump cost in local
                List<Label> labels = GetNext().labels;
                LocalBuilder jumpCostInstance = generator.DeclareLocal(typeof(float));
                done = false;

                foreach (CodeInstruction instr in jumpCost.SyncedEntry.GetValueIL())
                {
                    if (!done)
                    {
                        instr.labels.AddRange(labels);
                        done = true;
                    }
                    yield return instr;
                }
                foreach (CodeInstruction instr in airJumpCost.SyncedEntry.GetValueIL())
                {
                    yield return instr;
                }
                yield return ldthis.Clone();
                yield return CodeInstruction.LoadField(typeof(controls), "airJumps");
                yield return new CodeInstruction(OpCodes.Conv_R4);
                yield return new CodeInstruction(OpCodes.Mul);
                yield return new CodeInstruction(OpCodes.Add);
                yield return new CodeInstruction(OpCodes.Stloc, jumpCostInstance.LocalIndex);

                //skip stamina costs if jump cost <= 0
                yield return new CodeInstruction(OpCodes.Ldloc, jumpCostInstance.LocalIndex);
                yield return ldc0.Clone();
                yield return skipJumpIf.Clone();

                // subtract local from stamina
                yield return ldthis.Clone();
                yield return GetNext(); //dup
                yield return GetNext(); //loads stamina
                SkipX(7);
                yield return new CodeInstruction(OpCodes.Ldloc, jumpCostInstance.LocalIndex);
                yield return GetNext(); //sub
                yield return GetNext(); //stfld stamina

                //don't need to touch rest


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

                //only check stamina < 0 if sprinting has a stamina cost
                skipStaminaCheck = generator.DefineLabel();
                foreach (CodeInstruction instr in sprintCost.SyncedEntry.GetValueIL())
                {
                    yield return instr;
                }
                yield return new CodeInstruction(OpCodes.Ldc_R4, 0f);
                yield return new CodeInstruction(OpCodes.Beq, skipStaminaCheck);
                foreach (CodeInstruction instr in PassX(4))
                {
                    yield return instr;
                }

                last = GetNext();
                last.labels.Add(skipStaminaCheck);
                yield return last;

                //get cost from synced value
                foreach (CodeInstruction instr in LoopWhile(NextStamina))
                {
                    yield return instr;
                    last = instr;
                }
                yield return GetNext();
                SkipX(1);
                foreach (CodeInstruction instr in sprintCost.SyncedEntry.GetValueIL())
                {
                    yield return instr;
                }
                foreach (CodeInstruction instr in PassX(3))
                {
                    yield return instr;
                }

                //only set stamina regen to 1 if sprinting has a stamina cost
                skipStaminaCheck = generator.DefineLabel();
                foreach (CodeInstruction instr in sprintCost.SyncedEntry.GetValueIL())
                {
                    yield return instr;
                }
                yield return new CodeInstruction(OpCodes.Ldc_R4, 0f);
                yield return new CodeInstruction(OpCodes.Ble, skipStaminaCheck);

                Label elseLabel = generator.DefineLabel();
                //if sprint disables regen
                foreach (CodeInstruction instr in sprintDisablesRegen.SyncedEntry.GetValueIL())
                {
                    yield return instr;
                }
                yield return new CodeInstruction(OpCodes.Brfalse, elseLabel);
                foreach (CodeInstruction instr in PassX(3))
                {
                    yield return instr;
                }
                yield return new CodeInstruction(OpCodes.Br, skipStaminaCheck);

                CodeInstruction temp = ldthis.Clone();
                temp.labels.Add(elseLabel);
                yield return temp;
                yield return new CodeInstruction(OpCodes.Ldc_R4, 1f);
                yield return CodeInstruction.StoreField(typeof(controls), "staminaRegenSpeed");
                last = GetNext();
                last.labels.Add(skipStaminaCheck);
                yield return last;

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
                CodeInstruction ldthis = new CodeInstruction(OpCodes.Ldarg_0);

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

                CodeInstruction temp = GetNext();

                //only check stamina < 0 if initial draw has a stamina cost
                Label skipStaminaCheck = generator.DefineLabel();
                bool done = false;
                foreach (CodeInstruction instr in drawCost.SyncedEntry.GetValueIL())
                {
                    if (!done)
                    {
                        instr.labels.AddRange(temp.labels);
                        temp.labels = new() { };
                    }
                    yield return instr;
                }
                yield return new CodeInstruction(OpCodes.Ldc_R4, 0f);
                yield return new CodeInstruction(OpCodes.Ble, skipStaminaCheck);
                yield return temp.Clone();
                foreach (CodeInstruction instr in PassX(3))
                {
                    yield return instr;
                }
                CodeInstruction last = GetNext();
                last.labels.Add(skipStaminaCheck);
                yield return last;

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

                //store new drain in local variable
                yield return new CodeInstruction(OpCodes.Ldc_R4, 0f);
                yield return ldthis.Clone();
                yield return CodeInstruction.LoadField(typeof(controls), "holdtime");
                yield return new CodeInstruction(OpCodes.Ldc_R4, 1f);
                yield return new CodeInstruction(OpCodes.Sub);
                foreach (CodeInstruction instr in holdCost.SyncedEntry.GetValueIL())
                {
                    yield return instr;
                }
                yield return new CodeInstruction(OpCodes.Ldc_R4, 3f);
                yield return new CodeInstruction(OpCodes.Div);
                yield return new CodeInstruction(OpCodes.Mul);
                foreach (CodeInstruction instr in drawCost.SyncedEntry.GetValueIL())
                {
                    yield return instr;
                }
                yield return new CodeInstruction(OpCodes.Add);
                yield return CodeInstruction.Call(typeof(UnityEngine.Mathf), "Max", parameters: new[] { typeof(float), typeof(float)});
                yield return new CodeInstruction(OpCodes.Stloc, drain.LocalIndex);

                
                foreach (CodeInstruction instr in PassX(7))
                {
                    yield return instr;
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
                yield return startCheck;
                yield return ld0float.Clone();
                yield return new CodeInstruction(OpCodes.Ble, newLabel);

                //stamina > 0
                yield return ldthis.Clone();
                yield return ldStamina;
                yield return ld0float;
                yield return skipIf;

                yield return otherConds;
                foreach (CodeInstruction instr in LoopWhile(instr => instr.opcode != OpCodes.Stfld || instr.operand.ToString() != "System.Single holdtime"))
                {
                    yield return instr;
                }

                
                //reduce stamina by drain * deltatime, only if drain > 0

                //drain > 0?
                yield return new CodeInstruction(OpCodes.Ldloc, drain.LocalIndex);
                yield return ld0float.Clone();
                Label endTakeStamina = generator.DefineLabel();
                yield return new CodeInstruction(OpCodes.Ble_Un, endTakeStamina);
                
                //load stamina
                foreach(CodeInstruction instr in PassX(4))
                {
                    yield return instr;
                }
                //use drain instead of original calculation
                yield return new CodeInstruction(OpCodes.Ldloc, drain.LocalIndex);
                SkipX(4);

                //Navigate to end of stamina if branch
                foreach (CodeInstruction instr in PassX(6))
                {
                    yield return instr;
                }

                //add label for branch
                temp = GetNext();
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
