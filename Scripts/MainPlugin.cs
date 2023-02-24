using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Receiver2ModdingKit;
using Receiver2;
using UnityEngine;
using TubeMagazineLib;
using RewiredConsts;
using System.Reflection;
using Receiver2ModdingKit.CustomSounds;
using Wolfire;
using System.Numerics;
using Vector3 = UnityEngine.Vector3;
using Quaternion = UnityEngine.Quaternion;

namespace M3SUPER90_Plugin
{
    public class M3S90_Script : ModGunScript
    {
        private enum ActionState
        {
            Locked,
            Locking,
            Unlocked,
            Unlocking,
            UnlockingPartial,
            LockingPartial
        };

        private static MethodInfo getLastBullet;
        private static FieldInfo bullet_shake_time;
        private static System.Type BulletInventory;

        private float hammer_accel = -5000;

        private ModHelpEntry help_entry;
        public Sprite help_entry_sprite;

        public InventorySlot feeder;
        private static LinearMover action_slide = new LinearMover();
        private static ActionState action_state;
        public GameObject tube_magazine;
        private new TubeMagazineScript magazine;
        private FieldInfo _extractor_rod_stage;
        private static RotateMover locking_lever = new RotateMover();
        private RotateMover selector = new RotateMover();
        private Transform handguard;
        private int current_fire_mode = 1;
        private float round_eject;
        private bool fire_mode_switched;
        private bool selector_rotated;
        private bool hammer_moving;

        private bool carrierReady = true;

        private ExtractorRodStage extractor_rod_stage
        {
            get { return (ExtractorRodStage)_extractor_rod_stage.GetValue(this); }
            set { _extractor_rod_stage.SetValue(this, value); }
        }

        private readonly float[] slide_push_hammer_curve = new float[]
        {
            0,
            0,
            0.3f,
            1
        };
        private readonly float[] locking_lever_hammer_curve = new float[]
        {
            0.6f,
            1,
            1,
            0
        };
        public override ModHelpEntry GetGunHelpEntry()
        {
            return help_entry = new ModHelpEntry("M3S90")
            {
                info_sprite = help_entry_sprite,
                title = "Benelli M3 Super 90",
                description = "Benelli M3 Super 90 Dual-Mode 12 gauge shotgun\n"
                            + "Capacity: 6 + 1, 12 Gauge\n"
                            + "\n"
                            + "The Benelli M3 is a shotgun designed and manufactured by Italian manufacturer Benelli Armi SpA since 1989. Its most notable feature is that it allows the user to choose between a semi-automatic, and pump-action operation.\n"
                            + "\n"
                            + "The Super 90 variant of the M3 is the most common, and features a smaller body. This variant is available with various barrel lenghts and stock options, such as a fixed butt and pistol grip, or a top-folding butt and pistol grip.\n"
                            + "\n"
                            + "The pump-action mode of the M3 allows the user to fire low-pressure shells, such as dragons breath, which would fail to feed if fired in semi-auto mode."
            };
        }
        public override LocaleTactics GetGunTactics()
        {
            return new LocaleTactics()
            {
                gun_internal_name = InternalName,
                title = "Benelli M3 Super 90\n",
                text = "A modded dual-mode shotgun, made while half-awake\n" +
                       "A 12 gauge shot that allows the user to switch between semi-auto and pump-action mode.\n" +
                       "\n" +
                       "To safely holster the M3, flip on the safety."
            };
        }
        public override CartridgeSpec GetCustomCartridgeSpec()
        {
            return new CartridgeSpec()
            {
                extra_mass = 39f,
                mass = 1f,
                speed = 320f,
                diameter = 0.0010f
            };
        }

        private void FireBulletShotgun(ShellCasingScript round)
        {
            chamber_check_performed = false;

            CartridgeSpec cartridge_spec = default;
            cartridge_spec.SetFromPreset(round.cartridge_type);
            LocalAimHandler holdingPlayer = GetHoldingPlayer();

            Vector3 direction = transform_bullet_fire.rotation * Vector3.forward;
            BulletTrajectory bulletTrajectory = BulletTrajectoryManager.PlanTrajectory(transform_bullet_fire.position, cartridge_spec, direction, right_hand_twist);

            if (ConfigFiles.global.display_trajectory_window && ConfigFiles.global.display_trajectory_window_show_debug)
            {
                bulletTrajectory.draw_path = BulletTrajectory.DrawType.Debug;
            }
            else if (round.tracer || GunScript.force_tracers)
            {
                bulletTrajectory.draw_path = BulletTrajectory.DrawType.Tracer;
                bulletTrajectory.tracer_fuse = true;
            }

            if (holdingPlayer != null)
            {
                bulletTrajectory.bullet_source = gameObject;
                bulletTrajectory.bullet_source_entity_type = ReceiverEntityType.Player;
            }
            else
            {
                bulletTrajectory.bullet_source = gameObject;
                bulletTrajectory.bullet_source_entity_type = ReceiverEntityType.UnheldGun;
            }
            BulletTrajectoryManager.ExecuteTrajectory(bulletTrajectory);

            rotation_transfer_y += UnityEngine.Random.Range(rotation_transfer_y_min, rotation_transfer_y_max);
            rotation_transfer_x += UnityEngine.Random.Range(rotation_transfer_x_min, rotation_transfer_x_max);
            recoil_transfer_x -= UnityEngine.Random.Range(recoil_transfer_x_min, recoil_transfer_x_max);
            recoil_transfer_y += UnityEngine.Random.Range(recoil_transfer_y_min, recoil_transfer_y_max);
            add_head_recoil = true;

            if (CanMalfunction && malfunction == GunScript.Malfunction.None && (UnityEngine.Random.Range(0f, 1f) < doubleFeedProbability || force_double_feed_failure))
            {
                if (force_double_feed_failure && force_just_one_failure)
                {
                    force_double_feed_failure = false;
                }
                malfunction = GunScript.Malfunction.DoubleFeed;
                ReceiverEvents.TriggerEvent(ReceiverEventTypeInt.GunMalfunctioned, 2);
            }

            ReceiverEvents.TriggerEvent(ReceiverEventTypeVoid.PlayerShotFired);

            if (current_fire_mode == 0) action_state = ActionState.Unlocking;

            last_time_fired = Time.time;
            last_frame_fired = Time.frameCount;
            dry_fired = false;

            if (shots_until_dirty > 0)
            {
                shots_until_dirty--;
            }

            yoke_stage = YokeStage.Closed;
        }
        private void TryFireBulletShotgun()
        {
            Vector3 originalRotation = transform.Find("point_bullet_fire").localEulerAngles;

            transform_bullet_fire.localEulerAngles += new Vector3(
                UnityEngine.Random.Range(-0.2f, 0.2f),
                UnityEngine.Random.Range(-0.2f, 0.2f),
                0
            );

            TryFireBullet(1);

            if (dry_fired) return;

            transform_bullet_fire.localEulerAngles = originalRotation;

            for (int i = 0; i < 7; i++)
            {
                float angle = UnityEngine.Random.Range(0f, (float)System.Math.PI * 2);
                float diversion = UnityEngine.Random.Range(0f, 2f);

                float moveX = Mathf.Sin(angle) * diversion;
                float moveY = Mathf.Cos(angle) * diversion;

                transform_bullet_fire.localEulerAngles += new Vector3(
                    moveX,
                    moveY,
                    0
                );

                FireBulletShotgun(round_in_chamber);

                transform_bullet_fire.localEulerAngles = originalRotation;
            }
        }

        private System.Collections.IEnumerator moveRoundOnCarrier(ShellCasingScript round)
        {
            carrierReady = false;

            while (round.transform.localPosition.z != 0 || round.transform.localRotation.z != 0)
            {
                round.transform.localPosition = Vector3.MoveTowards(round.transform.localPosition, Vector3.zero, Time.deltaTime);
                round.transform.localRotation = Quaternion.RotateTowards(round.transform.localRotation, Quaternion.identity, Time.deltaTime * 200);

                yield return null;
            }

            carrierReady = true;

            yield break;
        }

        private System.Collections.IEnumerator moveRoundToChamber(ShellCasingScript round)
        {
            while (action_slide.amount > 0.90f)
            {
                yield return null;
            }

            Transform bolt = action_slide.transform.Find("bolt/point_round_chambered");

            round.transform.parent = bolt;

            round.transform.rotation = transform.rotation;

            while (action_slide.amount > 0)
            {
                round.transform.localRotation = Quaternion.identity;

                round.transform.localPosition = new Vector3(
                    0,
                    Mathf.Lerp(round.transform.localPosition.y, 0, action_slide.amount),
                    0
                );

                yield return null;
            }

            yield break;
        }

        public override void InitializeGun()
        {
            pooled_muzzle_flash = ((GunScript)ReceiverCoreScript.Instance().generic_prefabs.First(it => { return it is GunScript && ((GunScript)it).gun_model == GunModel.Deagle; })).pooled_muzzle_flash;
            //loaded_cartridge_prefab = ((GunScript)ReceiverCoreScript.Instance().generic_prefabs.First(it => { return it is GunScript && ((GunScript)it).gun_model == GunModel.Glock; })).loaded_cartridge_prefab;
        }
        public override void AwakeGun()
        {
            _extractor_rod_stage = typeof(GunScript).GetField("extractor_rod_stage", BindingFlags.Instance | BindingFlags.NonPublic);
            hammer.amount = 0f;

            getLastBullet = typeof(LocalAimHandler).GetMethod("GetLastMatchingLooseBullet", BindingFlags.NonPublic | BindingFlags.Instance);

            bullet_shake_time = typeof(LocalAimHandler).GetField("show_bullet_shake_time", BindingFlags.NonPublic | BindingFlags.Instance);

            BulletInventory = typeof(LocalAimHandler).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Instance).Single(t => t.Name == "BulletInventory");

            action_slide.transform = transform.Find("action_slide");
            action_slide.positions[0] = transform.Find("action_slide_forward").localPosition;
            action_slide.positions[1] = transform.Find("action_slide_back").localPosition;

            magazine = tube_magazine.GetComponent<TubeMagazineScript>();

            locking_lever.transform = transform.Find("locking_lever");
            locking_lever.rotations[0] = transform.Find("locking_lever_locked").localRotation;
            locking_lever.rotations[1] = transform.Find("locking_lever_unlocked").localRotation;

            handguard = transform.Find("handguard");

            selector.transform = transform.Find("handguard/selector");
            selector.rotations[0] = transform.Find("handguard/selector_resting").localRotation;
            selector.rotations[1] = transform.Find("handguard/selector_counter_clockwise").localRotation;

            Vector3 vector3 = Vector3.Normalize(action_slide.positions[1] - action_slide.positions[0]);
            float num7 = Vector3.Dot(vector3, action_slide.positions[0]);
            float num8 = Vector3.Dot(vector3, action_slide.positions[1]);
            float num9 = Vector3.Dot(vector3, transform.Find("action_slide_stopped").localPosition);
            float num10 = Vector3.Dot(vector3, transform.Find("action_slide_unstopped").localPosition);
            round_eject = Vector3.Dot(vector3, transform.Find("frame/point_round_eject").localPosition);
            float num13 = num8 - num7;
            slide_lock_position = (num9 - num7) / num13;
            slide_unlock_position = (num10 - num7) / num13;
            /*float num11 = Vector3.Dot(vector3, transform.Find("action_slide_stove_pipe").localPosition);
            slide_stovepipe_position = (num11 - num7) / num13;*/
        }

        public override void UpdateGun()
        {
            LocalAimHandler lah = LocalAimHandler.player_instance;

            if (lah.IsHoldingGun)
            {

                if (lah.IsAiming())
                {
                    transform.Find("pose_eject_rounds").localPosition = transform.Find("pose_eject_rounds_aiming").localPosition;
                    transform.Find("pose_eject_rounds").localRotation = transform.Find("pose_eject_rounds_aiming").localRotation;
                }
                else
                {
                    transform.Find("pose_eject_rounds").localPosition = transform.Find("pose_eject_rounds_hip").localPosition;
                    transform.Find("pose_eject_rounds").localRotation = transform.Find("pose_eject_rounds_hip").localRotation;
                }

                if (lah.character_input.GetButtonDown(RewiredConsts.Action.Hammer) || lah.character_input.GetButtonUp(RewiredConsts.Action.Hammer))
                {
                    ToggleSelector();
                }

                if (current_fire_mode == 1)
                {
                    var vector = transform.Find("handguard_start_position_pump").localPosition;
                    vector.z = vector.z - (action_slide.amount * (action_slide.positions[0].z - action_slide.positions[1].z));
                    handguard.localPosition = vector;
                }

                if (selector.amount == 0f) fire_mode_switched = false;

                if (selector.amount >= 0.9f && action_slide.amount == 0f)
                {
                    if (current_fire_mode == 0 && !fire_mode_switched)
                    {
                        fire_mode_switched = true;
                        current_fire_mode = 1;
                    }
                    if (current_fire_mode == 1 && !fire_mode_switched)
                    {
                        fire_mode_switched = true;
                        current_fire_mode = 0;
                        handguard.localPosition = transform.Find("handguard_start_position_semi").localPosition;
                    }

                }

                //Pump action open/close logic
                if (lah.character_input.GetButton(6) && lah.character_input.GetButton(10) && (action_state == ActionState.Locked || action_state == ActionState.UnlockingPartial))
                {
                    if (action_state != ActionState.UnlockingPartial) ModAudioManager.PlayOneShotAttached(sound_slide_back_partial, gameObject, 0.8f);
                    action_state = ActionState.UnlockingPartial;
                }
                else if (action_state == ActionState.UnlockingPartial)
                {
                    if (lah.character_input.GetButton(10))
                    {
                        action_state = ActionState.Unlocking;
                        ModAudioManager.PlayOneShotAttached(sound_slide_back_partial, gameObject, 0.5f);
                    }
                    else action_state = ActionState.LockingPartial;
                }
                else if (lah.character_input.GetButtonDown(10) || (current_fire_mode == 0 && lah.character_input.GetButtonUp(RewiredConsts.Action.Pull_Back_Slide)))
                {
                    if ((action_state == ActionState.Locked || action_state == ActionState.Locking))
                    {
                        action_state = ActionState.Unlocking;
                        ModAudioManager.PlayOneShotAttached(sound_slide_back, gameObject);
                    }
                    else if ((carrierReady || feeder.contents.Count == 0) && (action_state == ActionState.Unlocked || action_state == ActionState.Unlocking))
                    {
                        ModAudioManager.PlayOneShotAttached(sound_slide_released, gameObject, 0.7f);
                        action_state = ActionState.Locking;
                    }
                    else if (action_state == ActionState.UnlockingPartial)
                    {
                        action_state = ActionState.LockingPartial;
                    }
                    yoke_stage = YokeStage.Closed;
                }
                if (action_state == ActionState.Unlocked && !_slide_stop_locked && (lah.character_input.GetButtonDown(RewiredConsts.Action.Slide_Lock) || (current_fire_mode == 0 && !lah.character_input.GetButton(RewiredConsts.Action.Pull_Back_Slide))))
                {
                    action_state = ActionState.Locking;
                    if (current_fire_mode == 0 && this.CanMalfunction && this.malfunction == GunScript.Malfunction.None && (Probability.Chance(out_of_battery_probability) || force_out_of_battery))
                    {
                        if (force_out_of_battery && force_just_one_failure)
                        {
                            force_out_of_battery = false;
                        }
                        malfunction = GunScript.Malfunction.OutOfBattery;
                        action_state = ActionState.Locked;
                        action_slide.amount = 0.5f;
                        ReceiverEvents.TriggerEvent(ReceiverEventTypeInt.GunMalfunctioned, 4);
                    }
                    if (current_fire_mode == 0 && magazine.AmmoCount == 0 && feeder.contents.Count == 0)
                    {
                        action_state = ActionState.Unlocked;
                        action_slide.amount = slide_lock_position;
                        _slide_stop_locked = true;
                        ModAudioManager.PlayOneShotAttached(sound_slide_hit_lock, gameObject);
                    }

                    if (malfunction != Malfunction.OutOfBattery) ModAudioManager.PlayOneShotAttached(sound_slide_released, gameObject);
                }
                if (_slide_stop_locked)
                {
                    if (lah.character_input.GetButtonDown(RewiredConsts.Action.Slide_Lock))
                    {
                        action_state = ActionState.Locking;
                        ModAudioManager.PlayOneShotAttached(sound_slide_released, gameObject);
                    }
                    if (lah.character_input.GetButton(RewiredConsts.Action.Pull_Back_Slide))
                    {
                        action_state = ActionState.Unlocking;
                    }
                    if (action_slide.amount != slide_lock_position)
                    {
                        _slide_stop_locked = false;
                    }
                }

                // Ammo add toggle logic
                if (lah.character_input.GetButtonDown(11))
                {
                    if (yoke_stage == YokeStage.Closed) yoke_stage = YokeStage.Open;
                    else yoke_stage = YokeStage.Closed;
                }
                if (lah.character_input.GetButtonDown(20)) yoke_stage = YokeStage.Closed;

                // Ammo insert logic
                if (yoke_stage == YokeStage.Open && lah.character_input.GetButtonDown(70))
                {
                    if (action_state == ActionState.Locked && magazine.AmmoCount != magazine.maxRoundCapacity && magazine.Ready)
                    {
                        var bullet = getLastBullet.Invoke(lah, new object[]
                        {
                        new CartridgeSpec.Preset[] { loaded_cartridge_prefab.GetComponent<ShellCasingScript>().cartridge_type }
                        });

                        if (bullet != null)
                        {
                            ShellCasingScript round = (ShellCasingScript)BulletInventory.GetField("item", BindingFlags.Public | BindingFlags.Instance).GetValue(bullet);

                            magazine.AddRound(round);

                            if (magazine.AmmoCount == magazine.maxRoundCapacity) ModAudioManager.PlayOneShotAttached(sound_insert_mag_empty, gameObject);
                            else ModAudioManager.PlayOneShotAttached(sound_insert_mag_loaded, gameObject);

                            lah.MoveInventoryItem(round, magazine.slot);
                        }
                    }
                    else if (action_state == ActionState.Unlocked && feeder.contents.Count == 0)
                    {

                        var bullet = getLastBullet.Invoke(lah, new object[]
                        {
                        new CartridgeSpec.Preset[] { loaded_cartridge_prefab.GetComponent<ShellCasingScript>().cartridge_type }
                        });

                        if (bullet != null)
                        {
                            ShellCasingScript round = (ShellCasingScript)BulletInventory.GetField("item", BindingFlags.Public | BindingFlags.Instance).GetValue(bullet);

                            lah.MoveInventoryItem(round, feeder);

                            round.transform.parent = transform.Find("feeder/round_ready_slot");
                            round.transform.localScale = Vector3.one;
                            round.transform.localPosition = Vector3.zero;
                            round.transform.localRotation = Quaternion.identity;

                            ModAudioManager.PlayOneShotAttached(sound_start_insert_mag, round.gameObject);
                        }
                    }
                    else bullet_shake_time.SetValue(lah, Time.time);
                }
            }

            if (IsSafetyOn())
            { // Safety blocks the trigger from moving
                trigger.amount = Mathf.Min(trigger.amount, 0.1f);

                trigger.UpdateDisplay();
            }

            if (action_slide.amount == 0f)
            {
                if (this.malfunction == GunScript.Malfunction.None && this.sub_malfunction == GunScript.MalfunctionSub.FTF_Clearable)
                {
                    ReceiverEvents.TriggerEvent(ReceiverEventTypeInt.GunMalfunctionCleared, 3);
                    this.sub_malfunction = GunScript.MalfunctionSub.None;
                }
            }

            if (trigger.amount == 0f && action_slide.amount == 0f) _disconnector_needs_reset = false;

            // Fire logic
            if (trigger.amount == 1 && _hammer_state == 2 && !lah.character_input.GetButton(14) && action_slide.amount == 0 && !IsSafetyOn() && !_disconnector_needs_reset)
            {
                hammer_moving = true;
                hammer.amount = Mathf.MoveTowards(hammer.amount, 0, Time.deltaTime * 50);
                if (hammer.amount == 0)
                {
                    TryFireBulletShotgun();

                    _hammer_state = 0;

                    _disconnector_needs_reset = true;
                }
            }
            else hammer_moving = false;

            if (hammer_moving) locking_lever.amount = InterpCurve(locking_lever_hammer_curve, hammer.amount);
            else if (action_state == ActionState.Unlocking || action_state == ActionState.UnlockingPartial) locking_lever.amount = Mathf.MoveTowards(locking_lever.amount, 1f, Time.deltaTime * Time.timeScale * 50);
            else if (hammer.amount != 1f && action_state != ActionState.Unlocking && action_state != ActionState.UnlockingPartial) locking_lever.amount = Mathf.MoveTowards(locking_lever.amount, 0f, Time.deltaTime * Time.timeScale * 20);

            // Hammer cock by slide logic
            if (action_slide.amount > 0)
            {
                hammer.amount = Mathf.Max(hammer.amount, InterpCurve(slide_push_hammer_curve, action_slide.amount));

                _hammer_state = 2;
            }

            // Action open logic
            if (action_state == ActionState.Unlocking)
            {
                extractor_rod_stage = ExtractorRodStage.Open;

                if (action_slide.amount == 1)
                {
                    action_state = ActionState.Unlocked;
                    //ModAudioManager.PlayOneShotAttached("custom:/winchester/pumping/m1897_pump_backward_partialfull", __instance.gameObject, 0.45f);

                    if ((CanMalfunction && malfunction == Malfunction.None && Probability.Chance(GetFailureToFeedProbability())) || force_failure_to_feed)
                    {
                        if (force_failure_to_feed && force_just_one_failure)
                        {
                            force_failure_to_feed = false;
                        }

                        malfunction = Malfunction.FailureToFeed;
                        sub_malfunction = MalfunctionSub.FTF_Start;
                        ReceiverEvents.TriggerEvent(ReceiverEventTypeInt.GunMalfunctioned, 3);
                    }

                    if (locking_lever.amount == 1 && !(current_fire_mode == 0 && malfunction == Malfunction.FailureToFeed))
                    {
                        ShellCasingScript round;
                        if (magazine.CanRemoveRound() && feeder.contents.Count == 0 && carrierReady)
                        {
                            round = magazine.RemoveRound();

                            round.Move(feeder);

                            round.transform.parent = transform.Find("feeder/round_ready_slot");

                            StartCoroutine(moveRoundOnCarrier(round));
                        }
                    }
                }
                else
                {
                    action_slide.amount = Mathf.MoveTowards(action_slide.amount, 1, Time.deltaTime * 10);
                }

                if (round_in_chamber != null)
                {
                    Vector3 vector3 = Vector3.Normalize(action_slide.positions[1] - action_slide.positions[0]);
                    float round_travel = Vector3.Dot(vector3, round_in_chamber.transform.localPosition);

                    if (round_in_chamber != null && round_travel > round_eject)
                    {
                        EjectRoundInChamber(0.45f);
                    }

                    /*if (malfunction != Malfunction.Stovepipe)
                    {
                        if (action_slide.amount > slide_stovepipe_position)
                        {
                            if (current_fire_mode == 0 && this.CanMalfunction && action_slide.amount == 0f && (UnityEngine.Random.Range(0f, 1f) < stove_pipe_probability || this.force_stove_pipe_failure))
                            {
                                if (this.force_stove_pipe_failure && this.force_just_one_failure)
                                {
                                    this.force_stove_pipe_failure = false;
                                }
                                this.malfunction = GunScript.Malfunction.Stovepipe;
                                this.sub_malfunction = GunScript.MalfunctionSub.StovepipeStart;
                                ReceiverEvents.TriggerEvent(ReceiverEventTypeInt.GunMalfunctioned, 1);
                            }
                        }
                        else if (round_in_chamber != null && round_travel > round_eject && round_ready_to_eject)
                        {
                            EjectRoundInChamber(0.45f);
                            round_ready_to_eject = false;
                        }
                    }
                    else
                    {
                        action_state = ActionState.Locked;
                        action_slide.amount = slide_stovepipe_position;
                        this.ApplyTransform(("action_slide/round_stovepipe"), action_slide.amount, this.round_in_chamber.transform);
                        if (this.sub_malfunction == GunScript.MalfunctionSub.StovepipeClearable && action_slide.amount > this.slide_stovepipe_position)
                        {
                            this.EjectRoundInChamber(0.3f);
                            this.malfunction = GunScript.Malfunction.None;
                            ReceiverEvents.TriggerEvent(ReceiverEventTypeInt.GunMalfunctionCleared, 1);
                        }
                    }*/
                }
            }

            if (action_state == ActionState.UnlockingPartial)
            {
                action_slide.amount = Mathf.MoveTowards(action_slide.amount, press_check_amount, Time.deltaTime * 10);
            }

            if (action_state == ActionState.LockingPartial)
            {
                if (action_slide.amount == 0)
                {
                    action_state = ActionState.Locked;

                    ModAudioManager.PlayOneShotAttached(sound_slide_released, gameObject, 0.6f);
                }
                action_slide.amount = Mathf.MoveTowards(action_slide.amount, 0f, Time.deltaTime * 10);
            }

            if (carrierReady && malfunction == Malfunction.FailureToFeed)
            {
                if (action_slide.amount == 0f)
                {
                    if (sub_malfunction == MalfunctionSub.FTF_Clearable)
                    {
                        malfunction = Malfunction.None;
                    }
                }
                else if (sub_malfunction == MalfunctionSub.FTF_Start)
                {
                    sub_malfunction = MalfunctionSub.FTF_Clearable;
                }
            }

            // Action close logic
            if (action_state == ActionState.Locking)
            {
                extractor_rod_stage = ExtractorRodStage.Closed;

                if (action_slide.amount == 0)
                {
                    action_state = ActionState.Locked;

                    if (round_in_chamber != null)
                    {
                        round_in_chamber.transform.parent = action_slide.transform.Find("bolt/point_round_chambered");

                        round_in_chamber.transform.localPosition = Vector3.zero;
                        round_in_chamber.transform.localRotation = Quaternion.identity;

                    }

                    //ModAudioManager.PlayOneShotAttached("custom:/winchester/pumping/m1897_pump_forward_partialfull", __instance.gameObject, 0.45f);
                }
                else
                {
                    action_slide.amount = Mathf.MoveTowards(action_slide.amount, 0, Time.deltaTime * 10);
                }

                float round_eject = Vector3.Dot(transform.Find("frame/point_round_eject").position, transform.forward);

                if (feeder.contents.Count > 0 && round_in_chamber == null && !(current_fire_mode == 0 && malfunction == Malfunction.FailureToFeed))
                {
                    ShellCasingScript round = (ShellCasingScript)feeder.contents.ElementAt(0);

                    StartCoroutine(moveRoundToChamber(round));

                    ReceiveRound(round);

                    round.transform.parent = feeder.transform;
                }
            }

            if (malfunction == Malfunction.OutOfBattery)
            {
                if (action_slide.amount != 0.5f)
                {
                    malfunction = Malfunction.None;
                }
            }

            // Gun Animations
            if (action_state == ActionState.Locking) ApplyTransform("feeder_animation", action_slide.amount, transform.Find("feeder"));
            if (yoke_stage == YokeStage.Open) ApplyTransform("feeder_animation", magazine.loadProgress, transform.Find("feeder"));
            ApplyTransform("bolt_animation", action_slide.amount, transform.Find("action_slide/bolt"));
            ApplyTransform("shell_stop_animation", locking_lever.amount, transform.Find("shell_stop"));

            //Movers update
            trigger.UpdateDisplay();
            locking_lever.UpdateDisplay();
            selector.UpdateDisplay();
            selector.TimeStep(Time.deltaTime * 10);
            action_slide.UpdateDisplay();
            action_slide.TimeStep(Time.deltaTime * 10);
            hammer.UpdateDisplay();
        }
        private void ToggleSelector()
        {
            selector.asleep = false;
            if (selector.target_amount == 1f)
            {
                selector.target_amount = 0f;
                selector.accel = -1f;
                selector.vel = -10f;
                ModAudioManager.PlayOneShotAttached(sound_eject_mag_loaded, gameObject, 1f);
                selector_rotated = false;
            }
            else if (!selector_rotated)
            {
                selector.target_amount = 1f;
                selector.accel = 1;
                selector.vel = 10;
                ModAudioManager.PlayOneShotAttached(sound_eject_mag_empty, gameObject, 1f);
                selector_rotated = true;
            }
        }
    }
}
