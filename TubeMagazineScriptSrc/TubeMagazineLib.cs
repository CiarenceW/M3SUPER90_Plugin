using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Receiver2;
using UnityEngine;
using SimpleJSON;
using Receiver2ModdingKit;
using BepInEx.Configuration;
using BepInEx;
using System.IO;

namespace TubeMagazineLib
{
    [BepInPlugin("tubemagscript", "Tube Magazine Script", "1.0.0")]
    public class Loader : BaseUnityPlugin
    {
        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin Tube Magazine Script is loaded!");
        }
    }
    public class TubeMagazineScript : MonoBehaviour
    {
        public int maxRoundCapacity; //to avoid instanciating a variable, the variable below is a static, however, you can't interact with static variables in the Unity Inspector, so we need to make a normal public one, which we'll then assign to the static version.
        public static int kMaxRound;
        private Stack<ShellCasingScript> rounds;
        private Transform[] roundPositions;
        private Transform loadPosition;
        private Transform follower;
        public Transform round_start;
        private Vector3 followerStartPosition;
        private GunScript parent;
        [HideInInspector]
        public GameObject round_prefab;

        public float speed_mul = 14.5f;

        [HideInInspector]
        public float loadProgress = 1;
        public InventorySlot slot;

        public ConfigFile customPersistentData;
        public ConfigEntry<bool> roundChambered;
        public ConfigEntry<int> magazineAmmoCount;

        public int AmmoCount
        {
            get { return rounds.Count; }

            set
            {
                rounds.Clear();

                for (int i = 0; i < value; i++)
                {
                    ShellCasingScript round = round_prefab.GetComponent<ShellCasingScript>();

                    AddRound(Instantiate(round_prefab.GetComponent<ShellCasingScript>()));
                }
            }
        }

        public bool Ready
        {
            get { return loadProgress == 1; }
        }

        private void Awake()
        {
            parent = transform.parent.GetComponent<GunScript>();

            customPersistentData = new ConfigFile(Path.Combine(Paths.ConfigPath, transform.parent.GetComponent<GunScript>().InternalName + "_magazine_saved.cfg"), true);

            magazineAmmoCount = customPersistentData.Bind("Ammo",
           "RoundInMagAmount",
           0,
           "How many ammo is in the mag");

            roundChambered = customPersistentData.Bind("Ammo",
            "IsThereAChamberedRound",
            false,
            "Does the gun have a chambered round");

            kMaxRound = maxRoundCapacity;

            rounds = new Stack<ShellCasingScript>(kMaxRound);

            roundPositions = new Transform[kMaxRound];

            round_prefab = parent.loaded_cartridge_prefab;

            if (slot == null) slot = base.GetComponent<InventorySlot>();

            loadPosition = transform.Find("round_load");
            follower = transform.Find("follower");
            followerStartPosition = transform.Find("follower_start").localPosition;

            if (round_start == null)
            {
                for (int i = 0; i < kMaxRound; i++)
                {
                    roundPositions[i] = transform.Find("round_" + i.ToString());
                }
            }
            else
                try
                {
                    {
                        roundPositions[0] = round_start;
                        BoxCollider roundSize = round_prefab.GetComponent<BoxCollider>();
                        for (int i = 1; i < kMaxRound; i++)
                        {
                            var round_object = new GameObject("round_" + (i + 1));
                            round_object.transform.parent = transform;
                            roundPositions[i] = round_object.transform;
                            Vector3 vector = roundPositions[i].localPosition;
                            vector.y = roundPositions[i - 1].localPosition.y - roundSize.size.z;
                            roundPositions[i].localPosition = vector;
                        }
                    }
                }
                catch (IndexOutOfRangeException e)
                {
                    Debug.LogError(e.InnerException);
                    Debug.LogError(e.Data);
                }

            if (!roundPositions.Any(tr => tr == null))
            {
                Debug.Log("Successfully configured round positions for " + parent.InternalName);
            }
            else
            {
                Debug.LogError("Something went wrong while configuring round positions for " + parent.InternalName);
            }
            AmmoCount = magazineAmmoCount.Value;
            if (roundChambered.Value)
            {
                Transform bolt = transform.parent.Find("action_slide/bolt/point_round_chambered");

                parent.ReceiveRound(Instantiate(round_prefab.GetComponent<ShellCasingScript>()));

                var round = parent.round_in_chamber;

                round.transform.parent = bolt;

                round.transform.localPosition = Vector3.zero;

                round.transform.localRotation = Quaternion.identity;
            }
        }

        public void Fill()
        {
            while (!IsFull())
            {
                AddRound(Instantiate(round_prefab.GetComponent<ShellCasingScript>()));
            }
        }

        public bool IsFull()
        {
            return rounds.Count == kMaxRound;
        }

        private void UpdateRoundPositions()
        {
            if (ConfigFiles.global.infinite_ammo)
            {
                Fill();
            }

            try
            {
                for (int i = 0; i < rounds.Count; i++)
                {
                    if (i == 0 && loadProgress != 1) continue;
                    if (rounds.ElementAt(i).transform.localPosition.y != roundPositions[i].localPosition.y)
                    {
                        rounds.ElementAt(i).transform.localPosition = new Vector3(
                            roundPositions[0].localPosition.x,
                            Mathf.MoveTowards(rounds.ElementAt(i).transform.localPosition.y, roundPositions[i].localPosition.y, Time.deltaTime * Time.timeScale),
                            roundPositions[0].localPosition.z
                        );
                        rounds.ElementAt(i).transform.localRotation = roundPositions[i].localRotation;
                    }
                }

                if (loadProgress != 1)
                {
                    rounds.ElementAt(0).transform.localPosition = Vector3.Lerp(loadPosition.localPosition, roundPositions[0].localPosition, loadProgress);
                    rounds.ElementAt(0).transform.localRotation = Quaternion.Lerp(loadPosition.localRotation, roundPositions[0].localRotation, loadProgress);

                    loadProgress = Mathf.MoveTowards(loadProgress, 1, Time.deltaTime * Time.timeScale * speed_mul);
                }

                follower.localPosition = new Vector3(
                    0,
                    Mathf.MoveTowards(follower.localPosition.y, followerStartPosition.y - (rounds.Count * round_prefab.GetComponent<BoxCollider>().size.z), Time.deltaTime * Time.timeScale),
                    0
                );
            }
            catch (Exception e)
            {
                Debug.LogError("Error Updating Round Positions");
                Debug.LogError(e.InnerException);
                Debug.LogError(e.Data);
            }
        }

        private void Update()
        {
            UpdateRoundPositions();
            UpdateCustomPersistentData();
        }

        public void AddRound(ShellCasingScript round)
        {
            try
            {
                if (round == null || rounds.Count >= kMaxRound) return;

                round.Move(slot);

                round.transform.parent = transform;
                round.transform.localScale = Vector3.one;

                round.transform.localPosition = loadPosition.localPosition;
                round.transform.localRotation = loadPosition.localRotation;

                rounds.Push(round);

                loadProgress = 0;
            }
            catch (Exception e)
            {
                Debug.LogError("Error Adding Rounds");
                Debug.LogError(e.InnerException);
                Debug.LogError(e.Data);
            }
        }

        public bool CanRemoveRound()
        {
            if (rounds.Count > 0) return true;
            return false;
        }
        public ShellCasingScript RemoveRound()
        {
            if (rounds.Count > 0) return rounds.Pop();
            return null;
        }
        public void UpdateCustomPersistentData()
        {
            roundChambered.Value = parent.round_in_chamber != null;
            magazineAmmoCount.Value = AmmoCount;
        }
    }
}