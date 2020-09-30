using Facepunch;
using System;
using System.Linq;
using System.Reflection;
using System.Globalization;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rust.Ai.HTN;

//Bug fix

namespace Oxide.Plugins
{
    [Info("BotSpawn", "Steenamaroo", "2.1.0", ResourceId = 0)] 
    [Description("Spawn tailored AI with kits at monuments, custom locations, or randomly.")]

    class BotSpawn : RustPlugin
    {
        [PluginReference] Plugin Kits, Spawns, HumanNPC, CustomLoot; 

        int no_of_AI;
        bool loaded;
        Single currentTime;
        static BotSpawn botSpawn;
        const bool True = true, False = false; 
        const object Null = null;
        const string permAllowed = "botspawn.allowed";
        static System.Random random = new System.Random();
        int GetRand(int l, int h) => random.Next(l, h);

        public Dictionary<string, PopInfo> popinfo = new Dictionary<string, PopInfo>(); 
        public class PopInfo
        {
            public int population;
            public int queued;
            public int spawnTracker;
        }
         
        public Dictionary<ulong, Timer> weaponCheck = new Dictionary<ulong, Timer>();
        public static string Get(ulong v) => RandomUsernames.Get((int)(v % 2147483647uL));
        bool HasPermission(string id, string perm) => permission.UserHasPermission(id, perm);
        public Dictionary<string, List<Vector3>> spawnLists = new Dictionary<string, List<Vector3>>();
        public bool IsNight => currentTime > configData.Global.NightStartHour || currentTime < configData.Global.DayStartHour;

        Dictionary<ulong, string> editing = new Dictionary<ulong, string>();
        public static Timer aridTimer, temperateTimer, tundraTimer, arcticTimer;
        public Dictionary<string, Timer> timers = new Dictionary<string, Timer>() { { "BiomeArid", aridTimer }, { "BiomeTemperate", temperateTimer }, { "BiomeTundra", tundraTimer }, { "BiomeArctic", arcticTimer } };
        bool IsAuth(BasePlayer player) => player?.net?.connection?.authLevel == 2;

        #region Data  
        class StoredData
        {
            public Dictionary<string, DataProfile> DataProfiles = new Dictionary<string, DataProfile>();
            public Dictionary<string, ProfileRelocation> MigrationDataDoNotEdit = new Dictionary<string, ProfileRelocation>();
        }

        public class ProfileRelocation
        {
            public Vector3 ParentMonument = new Vector3();
            public Vector3 Offset = new Vector3();
        }

        class DefaultData
        {
            public Events Events = new Events();
            public Dictionary<string, ConfigProfile> Monuments = botSpawn.GotMonuments;
            public Dictionary<string, BiomeProfile> Biomes = new Dictionary<string, BiomeProfile>()
            {
                {"BiomeArid", new BiomeProfile() },
                {"BiomeTemperate", new BiomeProfile() },
                {"BiomeTundra", new BiomeProfile() },
                {"BiomeArctic", new BiomeProfile() },
            };
        }

        class UpdateData
        {
            public Events Events = new Events();
            public Dictionary<string, ConfigProfile> Monuments = new Dictionary<string, ConfigProfile>();
            public Dictionary<string, BiomeProfile> Biomes = new Dictionary<string, BiomeProfile>()
            {
                {"BiomeArid", new BiomeProfile() },
                {"BiomeTemperate", new BiomeProfile() },
                {"BiomeTundra", new BiomeProfile() },
                {"BiomeArctic", new BiomeProfile() },
            };
        }

        class SpawnsData
        {
            public Dictionary<string, List<string>> CustomSpawnLocations = new Dictionary<string, List<string>>();
        }

        StoredData storedData = new StoredData();
        DefaultData defaultData;
        SpawnsData spawnsData = new SpawnsData();

        void SaveSpawns() => Interface.Oxide.DataFileSystem.WriteObject($"BotSpawn/{configData.DataPrefix}-SpawnsData", spawnsData);
        void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject($"BotSpawn/{configData.DataPrefix}-CustomProfiles", storedData);
            Interface.Oxide.DataFileSystem.WriteObject($"BotSpawn/{configData.DataPrefix}-DefaultProfiles", defaultData);
            Interface.Oxide.DataFileSystem.WriteObject($"BotSpawn/{configData.DataPrefix}-SpawnsData", spawnsData);
        }

        void ReloadData(string profile)
        {
            storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>($"BotSpawn/{configData.DataPrefix}-CustomProfiles");
            defaultData = Interface.Oxide.DataFileSystem.ReadObject<DefaultData>($"BotSpawn/{configData.DataPrefix}-DefaultProfiles");

            if (storedData.DataProfiles.ContainsKey(profile))
            {
                AllProfiles.Remove(profile);
                AddData(profile, storedData.DataProfiles[profile]);
            }
            DataProfile prof = null;
            Vector3 loc = Vector3.zero;
            if (defaultData.Monuments.ContainsKey(profile))
                prof = JsonConvert.DeserializeObject<DataProfile>(JsonConvert.SerializeObject(defaultData.Monuments[profile]));
            if (defaultData.Biomes.ContainsKey(profile))
                prof = JsonConvert.DeserializeObject<DataProfile>(JsonConvert.SerializeObject(defaultData.Biomes[profile]));

            if (prof != null)
            {
                loc = AllProfiles[profile].Location;
                AllProfiles[profile] = prof;
                AllProfiles[profile].Location = loc;
            }
        }

        Vector3 s2v(string input)
        {
            String[] p = input.Split(',');
            return new Vector3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2]));
        }
        Quaternion s2r(string input)
        {
            String[] p = input.Split(',');
            return new Quaternion(float.Parse(p[3]), float.Parse(p[4]), float.Parse(p[5]), float.Parse(p[6]));
        }
        #endregion

        void OnServerInitialized()
        {
            botSpawn = this;
            currentTime = TOD_Sky.Instance.Cycle.Hour;
            timer.Repeat(2f, 0, () => currentTime = TOD_Sky.Instance.Cycle.Hour);
            CheckMonuments(False);
            LoadConfigVariables();

            storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>($"BotSpawn/{configData.DataPrefix}-CustomProfiles");
            defaultData = Interface.Oxide.DataFileSystem.ReadObject<DefaultData>($"BotSpawn/{configData.DataPrefix}-DefaultProfiles");
            spawnsData = Interface.Oxide.DataFileSystem.ReadObject<SpawnsData>($"BotSpawn/{configData.DataPrefix}-SpawnsData");

            var files = Interface.Oxide.DataFileSystem.GetFiles("BotSpawn");
            StoredData storedUpdate;
            UpdateData defaultUpdate;
            string name;
            foreach (var file in files)
            {
                name = file.Substring(file.IndexOf("BotSpawn") + 9);
                name = name.Substring(0, name.Length - 5);
                if (file.Contains("-CustomProfiles"))
                {
                    storedUpdate = Interface.Oxide.DataFileSystem.ReadObject<StoredData>($"BotSpawn/{name}");
                    Interface.Oxide.DataFileSystem.WriteObject($"BotSpawn/{name}", storedUpdate);
                }
                if (file.Contains("-DefaultProfiles"))
                {
                    defaultUpdate = Interface.Oxide.DataFileSystem.ReadObject<UpdateData>($"BotSpawn/{name}");
                    Interface.Oxide.DataFileSystem.WriteObject($"BotSpawn/{name}", defaultUpdate);
                }
            }

            SaveData();
            SetupProfiles();
            timer.Once(10, () => timer.Repeat(10, 0, () => AdjustPopulation()));
            loaded = True;
        }

        void Init()
        {
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            };
            var filter = RustExtension.Filter.ToList();
            filter.Add("cover points");
            filter.Add("resulted in a conflict");
            RustExtension.Filter = filter.ToArray();
            no_of_AI = 0;
        }

        void Loaded()
        {
            ConVar.AI.npc_families_no_hurt = False;
            foreach (var entry in timers)
                spawnLists.Add(entry.Key, new List<Vector3>());

            lang.RegisterMessages(Messages, this);
            permission.RegisterPermission(permAllowed, this);
        }

        void Unload()
        {
            var filter = RustExtension.Filter.ToList();
            filter.Remove("cover points");
            filter.Remove("resulted in a conflict");
            RustExtension.Filter = filter.ToArray();
            Wipe();
        }

        void Wipe()
        {
            foreach (var bot in NPCPlayers.ToDictionary(pair => pair.Key, pair => pair.Value).Where(bot => bot.Value != null))
                NPCPlayers[bot.Key].Kill();
        }

        #region BiomeSpawnsSetup
        void GenerateSpawnPoints(string name, int number, Timer myTimer, int biomeNo)
        {
            int getBiomeAttempts = 0;
            var spawnlist = spawnLists[name];
            int halfish = Convert.ToInt16((ConVar.Server.worldsize / 2) / 1.1f);
            var rand = UnityEngine.Random.insideUnitCircle * halfish;

            if (AllProfiles[name].Kit.Count > 0 && Kits == null)
            {
                PrintWarning(lang.GetMessage("nokits", this), name);
                return;
            }

            timers[name] = timer.Repeat(0.01f, 0, () =>
            {
                bool finished = True;
                if (spawnlist.Count < number + 10)
                {
                    getBiomeAttempts++;
                    if (getBiomeAttempts > 200 && spawnlist.Count == 0)
                    {
                        PrintWarning(lang.GetMessage("noSpawn", this), name);
                        timers[name].Destroy();
                        return;
                    }
                    rand = UnityEngine.Random.insideUnitCircle * halfish;
                    Vector3 randomSpot = new Vector3(rand.x, 0, rand.y);
                    finished = False;
                    if (TerrainMeta.BiomeMap.GetBiome(randomSpot, biomeNo) > 0.5f)
                    {
                        var point = CalculateGroundPos(new Vector3(randomSpot.x, 200, randomSpot.z));
                        if (point != Vector3.zero)
                            spawnlist.Add(CalculateGroundPos(new Vector3(randomSpot.x, 200, randomSpot.z))); ;
                    }
                }
                if (finished)
                {
                    int i = 0;
                    var target = TargetAmount(AllProfiles[name]);
                    if (target > 0)
                    {
                        int amount = CanRespawn(name, target, False);
                        if (amount > 0)
                        {
                            timer.Repeat(2, amount, () =>
                            {
                                if (CanRespawn(name, 1, True) == 1)
                                {
                                    SpawnBots(name, AllProfiles[name], "biome", null, spawnlist[i], -1);
                                    i++;
                                }
                            });
                        }
                    }
                    timers[name].Destroy();
                }
            });
        }

        public bool HasNav(Vector3 pos)
        {
            NavMeshHit navMeshHit;
            return (NavMesh.SamplePosition(pos, out navMeshHit, 2, 1));
        }

        public static Vector3 CalculateGroundPos(Vector3 pos)
        {
            pos.y = TerrainMeta.HeightMap.GetHeight(pos);
            NavMeshHit navMeshHit;

            if (!NavMesh.SamplePosition(pos, out navMeshHit, 2, 1))
                pos = Vector3.zero;
            else if (WaterLevel.GetWaterDepth(pos, true) > 0)
                pos = Vector3.zero;
            else if (Physics.RaycastAll(navMeshHit.position + new Vector3(0, 100, 0), Vector3.down, 99f, 1235288065).Any())
                pos = Vector3.zero;
            else
                pos = navMeshHit.position;
            return pos;
        }

        Vector3 TryGetSpawn(Vector3 pos, int radius)
        {
            int attempts = 0;
            var spawnPoint = Vector3.zero;
            Vector2 rand;

            while (attempts < 200 && spawnPoint == Vector3.zero)
            {
                attempts++;
                rand = UnityEngine.Random.insideUnitCircle * radius;
                spawnPoint = CalculateGroundPos(pos + new Vector3(rand.x, 0, rand.y));
                if (spawnPoint != Vector3.zero)
                    return spawnPoint;
            }
            return spawnPoint;
        }

        int GetNextSpawn(string name, DataProfile profile)
        {
            if (profile.UseCustomSpawns && spawnsData.CustomSpawnLocations[name].Count > 0)
            {
                int num = spawnsData.CustomSpawnLocations[name].Count;
                popinfo[name].spawnTracker++;
                if (popinfo[name].spawnTracker >= Convert.ToInt16(num))
                    popinfo[name].spawnTracker = 0;
                return popinfo[name].spawnTracker;
            }
            return -1;
        }
        #endregion

        #region population

        void AdjustPop(string profile, int num) => popinfo[profile].population += num; 
        void AdjustQueue(string profile, int num) => popinfo[profile].queued += num;

        int TargetAmount(DataProfile profile) => IsNight ? profile.Night_Time_Spawn_Amount : profile.Day_Time_Spawn_Amount;

        int CanRespawn(string name, int amount, bool second)
        {
            int response = TargetAmount(AllProfiles[name]) - popinfo[name].population;
            if (!second)
                response += popinfo[name].queued;
            if (response > 0)
            {
                if (!second)
                    AdjustQueue(name, Mathf.Min(amount, response));
                return Mathf.Min(amount, response);
            }
            else if (second)
                AdjustQueue(name, -1);
            return 0;
        }

        Dictionary<string, List<int>> availableStatSpawns = new Dictionary<string, List<int>>();
        void AdjustPopulation()
        {
            foreach (var profile in AllProfiles.Where(x => x.Value.AutoSpawn == True && x.Key != "AirDrop")) //filter biome + airdrop?
            {
                int targetAmount = TargetAmount(profile.Value);
                var current = popinfo[profile.Key].population + popinfo[profile.Key].queued;
                if (current < targetAmount)
                {
                    popinfo[profile.Key].queued += targetAmount - current;
                    timer.Repeat(1f, targetAmount - current, () =>
                    {
                        if (profile.Value.UseCustomSpawns && spawnsData.CustomSpawnLocations.ContainsKey(profile.Key))
                        {
                            int spawnnum = 0, num = spawnsData.CustomSpawnLocations[profile.Key].Count;
                            if (num > 0)
                            {
                                if (!profile.Value.ChangeCustomSpawnOnDeath)
                                {
                                    spawnnum = availableStatSpawns[profile.Key][random.Next(availableStatSpawns[profile.Key].Count())];
                                    availableStatSpawns[profile.Key].Remove(spawnnum);
                                }
                                else
                                    spawnnum = GetNextSpawn(profile.Key, profile.Value);
                                SpawnBots(profile.Key, AllProfiles[profile.Key], null, null, new Vector3(), spawnnum);
                            }
                        }
                        else
                        {
                            if (timers.ContainsKey(profile.Key))
                            {
                                if (spawnLists[profile.Key].Count > 0)
                                    SpawnBots(profile.Key, AllProfiles[profile.Key], "biome", null, spawnLists[profile.Key][random.Next(spawnLists[profile.Key].Count)], -1);
                            }
                            else
                                SpawnBots(profile.Key, AllProfiles[profile.Key], null, null, new Vector3(), -1);
                        }
                    });
                    continue;
                }
                if (popinfo[profile.Key].population > targetAmount)
                {
                    foreach (var npc in NPCPlayers)
                    {
                        var bData = npc.Value.GetComponent<BotData>();
                        if (bData.monumentName == profile.Key && bData.respawn)
                        {
                            //if (timers.ContainsKey(bData.monumentName))
                            npc.Value.Kill();
                            break;
                        }
                    }
                }
            }
        }
        #endregion

        #region BotSetup 
        void DeployNpcs(Vector3 location, string name, DataProfile profile, string group, int num) => SpawnBots(name, profile, "Attack", group, location, num);
        void SpawnBots(string name, DataProfile zone, string type, string group, Vector3 location, int spawnNum)
        {
            bool respawn = type != "Attack" && type != "AirDrop";
            var pos = zone.Location;
            var finalPoint = Vector3.zero;
            bool stationary = zone.Stationary;
            Quaternion rot = new Quaternion();
            if (location == Vector3.zero && type != "AirDrop" && zone.UseCustomSpawns && spawnsData.CustomSpawnLocations[name].Count > 0)
            {
                var customLoc = spawnNum == -1 ? spawnsData.CustomSpawnLocations[name][random.Next(spawnsData.CustomSpawnLocations[name].Count)] : spawnsData.CustomSpawnLocations[name][spawnNum];
                Vector3 loc = s2v(customLoc);
                rot = s2r(customLoc);
                if (HasNav(loc) || zone.Stationary)
                    finalPoint = loc;
                else
                {
                    PrintWarning(lang.GetMessage("noNav", this), name, spawnsData.CustomSpawnLocations[name].IndexOf(customLoc)); 
                    return;
                }
            }
            else
                stationary = False;
            if (finalPoint == Vector3.zero)
            {
                if (location != Vector3.zero)
                    pos = location;
                if (type != "biome")
                    finalPoint = TryGetSpawn(pos, zone.Radius); 
                else
                    finalPoint = location;

                if (finalPoint == Vector3.zero)
                {
                    Puts($"Can't get spawn point at {name}. Skipping one npc."); 
                    AdjustQueue(name, -1);
                    return;
                }
            }

            if (zone.Chute && !stationary)
            {
                var rand = UnityEngine.Random.insideUnitCircle * zone.Radius;
                finalPoint = (type == "AirDrop") ? pos + new Vector3(rand.x, -40, rand.y) : new Vector3(finalPoint.x, 200, finalPoint.z);
            }

            NPCPlayer entity = (NPCPlayer)InstantiateSci(finalPoint, rot, zone.Murderer);
            var npc = entity.GetComponent<NPCPlayerApex>();
            npc.Spawn();

            NextTick(() =>
            {
                if (npc == null || npc.IsDestroyed || npc.IsDead())
                {
                    if (respawn)//popinfo.ContainsKey(name))
                        AdjustQueue(name, -1);
                    if (spawnNum != -1 && respawn)
                        availableStatSpawns[name].Add(spawnNum);
                    return;
                }
                if (!NPCPlayers.ContainsKey(npc.userID))
                    NPCPlayers.Add(npc.userID, npc);
                else
                {
                    npc.Kill();
                    PrintWarning(lang.GetMessage("dupID", this));
                    if (respawn)//popinfo.ContainsKey(name))
                        AdjustQueue(name, -1);
                    if (spawnNum != -1 && respawn)
                        availableStatSpawns[name].Add(spawnNum);
                    return;
                }
                timer.Once(1f, () =>
                {
                    if (npc != null)
                    {
                        npc.transform.SetPositionAndRotation(npc.transform.position, rot);
                        npc.SetFact(NPCPlayerApex.Facts.IsAggro, 0, False, False);
                    }
                });
                if (zone.Murderer)
                {
                    var suit = ItemManager.CreateByName("scarecrow.suit", 1, 0);
                    var eyes = ItemManager.CreateByName("gloweyes", 1, 0);
                    if (!suit.MoveToContainer(npc.inventory.containerWear))
                        suit.Remove();
                    if (!eyes.MoveToContainer(npc.inventory.containerWear))
                        eyes.Remove();
                }

                var bData = npc.gameObject.AddComponent<BotData>();
                bData.stationary = stationary;
                if (spawnNum != -1 && respawn)
                    bData.CustomSpawnNum = spawnNum;

                bData.monumentName = name;

                no_of_AI++;
                bData.respawn = True;
                bData.profile = zone.Clone();
                bData.group = group ?? null;
                bData.spawnPoint = finalPoint;
                bData.biome = type == "biome";

                npc.startHealth = zone.BotHealth;
                npc.InitializeHealth(zone.BotHealth, zone.BotHealth);

                npc.CommunicationRadius = 0;
                npc.AiContext.Human.NextToolSwitchTime = Time.realtimeSinceStartup * 10;
                npc.AiContext.Human.NextWeaponSwitchTime = Time.realtimeSinceStartup * 10;

                if (zone.Chute && !stationary)
                    AddChute(npc, finalPoint);

                int kitRnd;
                kitRnd = random.Next(zone.Kit.Count);

                if (zone.BotNames.Count == zone.Kit.Count && zone.Kit.Count != 0)
                    SetName(zone, npc, kitRnd);
                else
                    SetName(zone, npc, random.Next(zone.BotNames.Count));

                GiveKit(npc, zone, kitRnd);
                npc.clothingMoveSpeedReduction = -zone.Running_Speed_Boost;
                SortWeapons(npc);

                int suicInt = random.Next(zone.Suicide_Timer, zone.Suicide_Timer + 10);
                if (!respawn)
                {
                    bData.respawn = False;
                    RunSuicide(npc, suicInt);
                }
                else
                {
                    AdjustPop(name, 1);
                    AdjustQueue(name, -1);
                }

                if (zone.Disable_Radio)
                    npc.RadioEffect = new GameObjectRef();

                ToggleAggro(npc, Convert.ToByte(!zone.Peace_Keeper), zone.Aggro_Range);
                npc.Stats.DeaggroChaseTime = 10;
                npc.Stats.Defensiveness = 1;
            });
        }

        BaseEntity InstantiateSci(Vector3 position, Quaternion rotation, bool murd) 
        {
            string type = murd ? "murderer" : "scientist";
            string prefabname = $"assets/prefabs/npc/{type}/{type}.prefab"; 

            GameObject gameObject = Instantiate.GameObject(GameManager.server.FindPrefab(prefabname), position, rotation);
            gameObject.name = prefabname;
            SceneManager.MoveGameObjectToScene(gameObject, Rust.Server.EntityScene);
            if (gameObject.GetComponent<Spawnable>())
                UnityEngine.Object.Destroy(gameObject.GetComponent<Spawnable>());
            if (!gameObject.activeSelf)
                gameObject.SetActive(True);
            BaseEntity component = gameObject.GetComponent<BaseEntity>(); 
            return component;
        }

        void AddChute(NPCPlayerApex npc, Vector3 newPos)
        {
            float wind = random.Next(0, Mathf.Min(100, configData.Global.Max_Chute_Wind_Speed)) / 40f;
            float fall = random.Next(60, Mathf.Min(100, configData.Global.Max_Chute_Fall_Speed) + 60) / 20f;

            var rb = npc.gameObject.GetComponent<Rigidbody>();
            rb.isKinematic = False;
            rb.useGravity = False;
            rb.drag = 0f;
            npc.gameObject.layer = 0;//prevent_build layer fix
            var fwd = npc.transform.forward;
            rb.velocity = new Vector3(fwd.x * wind, 0, fwd.z * wind) - new Vector3(0, fall, 0);

            var col = npc.gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(1, 1f, 1);//feet above ground

            var Chute = GameManager.server.CreateEntity("assets/prefabs/misc/parachute/parachute.prefab", newPos, Quaternion.Euler(0, 0, 0));
            Chute.gameObject.Identity();
            Chute.SetParent(npc);
            Chute.Spawn();
        }

        void SetName(DataProfile zone, NPCPlayerApex npc, int number)
        {
            if (zone.BotNames.Count == 0 || zone.BotNames.Count <= number || zone.BotNames[number] == String.Empty)
            {
                npc.displayName = Get(npc.userID);
                npc.displayName = char.ToUpper(npc.displayName[0]) + npc.displayName.Substring(1);
            }
            else
                npc.displayName = zone.BotNames[number];

            if (zone.BotNamePrefix != String.Empty)
                npc.displayName = zone.BotNamePrefix + " " + npc.displayName;
            if (npc is Scientist)
                (npc as Scientist).LootPanelName = npc.displayName;
        }

        void GiveKit(NPCPlayerApex npc, DataProfile zone, int kitRnd)
        {
            if (npc == null || npc.inventory == null)
                return;
            var bData = npc.GetComponent<BotData>();
            string type = zone.Murderer ? "Murderer" : "Scientist";

            if (zone.Kit.Count != 0 && zone.Kit[kitRnd] != null)
            {
                object checkKit = Kits?.CallHook("GetKitInfo", zone.Kit[kitRnd], True);
                if (checkKit == null)
                {
                    PrintWarning($"Kit {zone.Kit[kitRnd]} does not exist - Spawning default {type}.");
                }
                else
                {
                    bool weaponInBelt = False;
                    JObject kitContents = checkKit as JObject;
                    if (kitContents != null)
                    {
                        JArray items = kitContents["items"] as JArray;
                        foreach (var weap in items)
                        {
                            JObject item = weap as JObject;
                            if (item["container"].ToString() == "belt")
                                weaponInBelt = True;
                        }
                    }
                    if (!weaponInBelt)
                    {
                        PrintWarning($"Kit {zone.Kit[kitRnd]} has no items in belt - Spawning default {type}.");
                    }
                    else
                    {
                        if (bData.profile.Keep_Default_Loadout == False)
                            npc.inventory.Strip();
                        Kits?.Call($"GiveKit", npc, zone.Kit[kitRnd], True);
                    }
                }
            }
        }

        void SortWeapons(NPCPlayerApex npc)
        {
            if (npc == null)
                return;
            var bData = npc.GetComponent<BotData>();
            ItemDefinition fuel = ItemManager.FindItemDefinition("lowgradefuel");
            foreach (var attire in npc.inventory.containerWear.itemList)
                if (attire.info.shortname.Equals("hat.miner") || attire.info.shortname.Equals("hat.candle"))
                {
                    bData.hasHeadLamp = True; 
                    Item newItem = ItemManager.Create(fuel, 1);
                    attire.contents.Clear();
                    if (!newItem.MoveToContainer(attire.contents))
                        newItem.Remove();
                    else
                    {
                        npc.SendNetworkUpdateImmediate();
                        npc.inventory.ServerUpdate(0f);
                    }
                }
            foreach (Item item in npc.inventory.containerBelt.itemList)//store organised weapons lists 
            {
                var held = item.GetHeldEntity();
                if (held != null && held as HeldEntity != null)
                {
                    if (held is FlameThrower || held.name.Contains("launcher"))
                        continue;
                    if (held as BaseMelee != null || held as TorchWeapon != null)
                    {
                        bData.Weapons[1].Add(item);
                        bData.Weapons[0].Add(item);
                    }
                    else if (held as BaseProjectile != null)
                    {
                        bData.Weapons[0].Add(item);
                        if (held.name.Contains("m92") || held.name.Contains("pistol") || held.name.Contains("python") || held.name.Contains("waterpipe"))
                            bData.Weapons[2].Add(item);
                        else if (held.name.Contains("bolt") || held.name.Contains("l96"))
                            bData.Weapons[4].Add(item);
                        else
                            bData.Weapons[3].Add(item);
                    }
                }
            }
            if ((npc is Scientist && (bData.Weapons[0].Count == 0 || (bData.Weapons[0].Count == 1 && bData.Weapons[1].Count == 1)))
                || npc is NPCMurderer && bData.Weapons[0].Count == 0)
            {
                PrintWarning(lang.GetMessage("noWeapon", this), bData.monumentName); 
                bData.noweapon = True;
                return;
            }
            npc.CancelInvoke(npc.EquipTest);
        }

        void RunSuicide(NPCPlayerApex npc, int suicInt)
        {
            if (!NPCPlayers.ContainsKey(npc.userID))
                return;
            timer.Once(suicInt, () =>
            {
                if (npc == null)
                    return;
                if (npc.AttackTarget != null && Vector3.Distance(npc.transform.position, npc.AttackTarget.transform.position) < 10 && npc.GetNavAgent.isOnNavMesh)
                {
                    var position = npc.AttackTarget.transform.position;
                    npc.svActiveItemID = 0;
                    npc.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                    npc.inventory.UpdatedVisibleHolsteredItems();
                    timer.Repeat(0.05f, 100, () =>
                    {
                        if (npc == null)
                            return;
                        npc.SetDestination(position);
                    });
                }
                timer.Once(4, () =>
                {
                    if (npc == null)
                        return;
                    Effect.server.Run("assets/prefabs/weapons/rocketlauncher/effects/rocket_explosion.prefab", npc.transform.position);
                    HitInfo nullHit = new HitInfo();
                    nullHit.damageTypes.Add(Rust.DamageType.Explosion, 10000); 
                    npc.IsInvinsible = False;
                    npc.Die(nullHit);
                }
                );
            });
        }
        #endregion

        static BasePlayer FindPlayerByName(string name)
        {
            BasePlayer result = null;
            foreach (BasePlayer current in BasePlayer.activePlayerList)
            {
                if (current.displayName.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || current.UserIDString.Contains(name, CompareOptions.OrdinalIgnoreCase)
                    || current.displayName.Contains(name, CompareOptions.OrdinalIgnoreCase))
                    result = current;
            }
            return result;
        }

        #region Hooks
        object OnNpcKits(ulong userID)
        {
            return NPCPlayers.ContainsKey(userID) ? True : Null;
        }

        private object CanBeTargeted(BaseCombatEntity player, BaseEntity entity)//stops autoturrets targetting bots
        {
            NPCPlayer npcPlayer = player as NPCPlayer;
            return (npcPlayer != null && NPCPlayers.ContainsKey(npcPlayer.userID) && configData.Global.Turret_Safe) ? False : Null;
        }

        private object CanBradleyApcTarget(BradleyAPC bradley, BaseEntity target)//stops bradley targeting bots
        {
            NPCPlayer npcPlayer = target as NPCPlayer;
            return (npcPlayer != null && NPCPlayers.ContainsKey(npcPlayer.userID) && configData.Global.APC_Safe) ? False : Null;
        }

        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var botMelee = info?.Initiator as BaseMelee;
            bool melee = False;
            if (botMelee != null)
            {
                melee = True;
                info.Initiator = botMelee.GetOwnerPlayer();
            }
            NPCPlayerApex bot = entity as NPCPlayerApex;
            BotData bData;

            //If victim is one of mine
            if (bot != null && NPCPlayers.ContainsKey(bot.userID))
            {
                var attackPlayer = info?.Initiator as BasePlayer;
                bData = bot.GetComponent<BotData>();

                if (configData.Global.Pve_Safe)
                {
                    if (info.Initiator?.ToString() == null && info.damageTypes.GetMajorityDamageType() == Rust.DamageType.Bullet)
                    {
                        return True; //new autoturrets
                    }
                    if (info.Initiator?.ToString() == null || info.Initiator.ToString().Contains("cactus") || info.Initiator.ToString().Contains("barricade"))
                    {
                        info.damageTypes.ScaleAll(0);
                        return True;
                    }
                }

                if (attackPlayer != null)
                {
                    if (NPCPlayers.ContainsKey(attackPlayer.userID))//and attacker is one of mine
                        if (attackPlayer.GetComponent<BotData>().monumentName == bData.monumentName)
                            return True; //wont attack their own

                    if (bData.profile.Peace_Keeper && (info.Weapon is BaseMelee || info.Weapon is TorchWeapon))//prevent melee farming with peacekeeper on
                    {
                        info.damageTypes.ScaleAll(0);
                        return True;
                    }

                    if (info != null && bData.profile.Die_Instantly_From_Headshot && info.isHeadshot)
                    {
                        var weap = info?.Weapon?.ShortPrefabName;
                        var weaps = bData.profile.Instant_Death_From_Headshot_Allowed_Weapons;

                        if (weaps.Count == 0 || weap != null && weaps.Contains(weap))
                        {
                            info.damageTypes.Set(0, bot.health);
                            return null;
                        }
                    }
                    if (Vector3.Distance(attackPlayer.transform.position, bot.transform.position) > bot.Stats.AggressionRange)
                    {
                        if (bot.Stats.AggressionRange < 400)
                        {
                            bot.Stats.AggressionRange += 400;
                            bot.Stats.DeaggroRange += 400;
                        }
                        ForceMemory(bot, attackPlayer);
                        timer.Repeat(1f, 20, () =>
                        {
                            if (bot != null)
                            {
                                bot.RandomMove();
                                if (bot.AttackTarget != null && bot.AttackTarget.IsVisible(bot.eyes.position, (bot.AttackTarget as BasePlayer).eyes.position, 400))
                                    Rust.Ai.HumanAttackOperator.AttackEnemy(bot.AiContext, Rust.Ai.AttackOperator.AttackType.LongRange);
                            }
                        });

                        timer.Once(20, () =>
                        {
                            if (bot == null)
                                return;
                            bot.Stats.AggressionRange = bData.profile.Aggro_Range;
                            bot.Stats.DeaggroRange += bData.profile.DeAggro_Range;
                        });
                    }

                    bot.AttackTarget = attackPlayer;
                    bot.lastAttacker = attackPlayer;
                    bData.goingHome = False;
                }
            }
            NPCPlayerApex attackNPC = info?.Initiator as NPCPlayerApex;
            //if attacker is one of mine
            if (attackNPC != null && entity is BasePlayer && NPCPlayers.ContainsKey(attackNPC.userID))
            {
                bData = attackNPC.GetComponent<BotData>();
                float rand = GetRand(1, 101);
                float distance = Vector3.Distance(info.Initiator.transform.position, entity.transform.position);

                float newAccuracy = bData.profile.Bot_Accuracy_Percent;
                float newDamage = bData.profile.Bot_Damage_Percent / 100f;
                if (distance > 100f && bData.enemyDistance != 4) //sniper exemption 
                {
                    newAccuracy = bData.profile.Bot_Accuracy_Percent / (distance / 100f);
                    newDamage = newDamage / (distance / 100f); 
                }
                if (!melee && newAccuracy < rand)
                    return True;
                info.damageTypes.ScaleAll(newDamage);
            }
            return null;
        }

        void OnPlayerDeath(NPCPlayerApex player, HitInfo info)=>OnEntityKill(player, info?.InitiatorPlayer != null);
        void OnEntityKill(NPCPlayerApex npc, bool killed)
        {
            if (npc?.userID != null && NPCPlayers.ContainsKey(npc.userID) && !botInventories.ContainsKey(npc.userID))
            {
                var bData = npc.GetComponent<BotData>();  
                if (bData == null)
                    return;

                if (bData.respawn)
                    AdjustPop(bData.monumentName, -1); 

                var pos = npc.transform.position;
                if (killed && bData.profile.Spawn_Hackable_Death_Crate_Percent > GetRand(1, 101) && npc.WaterFactor() < 0.1f) 
                {
                    timer.Once(2f, () =>
                    {
                        var Crate = GameManager.server.CreateEntity("assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab", pos + new Vector3(1, 2, 0), Quaternion.Euler(0, 0, 0));
                        Crate.Spawn();
                        (Crate as HackableLockedCrate).hackSeconds = HackableLockedCrate.requiredHackSeconds - bData.profile.Death_Crate_LockDuration;
                        timer.Once(1.4f, () =>
                        {
                            if (Crate == null)
                                return;  
                            if (CustomLoot && bData.profile.Death_Crate_CustomLoot_Profile != string.Empty)
                            {
                                var container = Crate?.GetComponent<StorageContainer>(); 
                                if (container != null)
                                {
                                    container.inventory.capacity = 36;
                                    container.onlyAcceptCategory = ItemCategory.All;
                                    container.SendNetworkUpdateImmediate();
                                    container.inventory.Clear();

                                    List<Item> loot = (List<Item>)CustomLoot?.Call("MakeLoot", bData.profile.Death_Crate_CustomLoot_Profile);
                                    if (loot != null)
                                        foreach (var item in loot)
                                            if (!item.MoveToContainer(container.inventory, -1, True))
                                                item.Remove();
                                }
                            }
                        });
                    });
                }
                Item activeItem = npc.GetActiveItem();

                if (bData.profile.Weapon_Drop_Percent >= GetRand(1, 101) && activeItem != null)
                {
                    var numb = GetRand(Mathf.Min(bData.profile.Min_Weapon_Drop_Condition_Percent, bData.profile.Max_Weapon_Drop_Condition_Percent), bData.profile.Max_Weapon_Drop_Condition_Percent);
                    numb = Convert.ToInt16((numb / 100f) * activeItem.maxCondition);
                    activeItem.condition = numb;
                    activeItem.Drop(npc.eyes.position, new Vector3(), new Quaternion());
                    npc.svActiveItemID = 0;
                    npc.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                }
                ItemContainer[] source = { npc.inventory.containerMain, npc.inventory.containerWear, npc.inventory.containerBelt };
                Inv botInv = new Inv() { profile = bData.profile };
                botInventories.Add(npc.userID, botInv);
                for (int i = 0; i < source.Length; i++)
                    foreach (var item in source[i].itemList)
                        botInv.inventory[i].Add(new InvContents
                        {
                            ID = item.info.itemid,
                            amount = item.amount,
                            skinID = item.skin,
                        });

                if (bData.profile.Disable_Radio == True)
                    npc.DeathEffect = new GameObjectRef();//kill radio effects 

                DeadNPCPlayerIds.Add(npc.userID);
                no_of_AI--;
                if (bData.respawn == False)
                    return;

                if (bData.biome && spawnLists.ContainsKey(bData.monumentName))
                {
                    List<Vector3> spawnList = new List<Vector3>();
                    spawnList = spawnLists[bData.monumentName];
                    int spawnPos = random.Next(spawnList.Count);
                    if (CanRespawn(bData.monumentName, 1, False) == 1)
                    {
                        timer.Once(bData.profile.Respawn_Timer, () =>
                        {
                            if (AllProfiles.ContainsKey(bData.monumentName))
                            {
                                if (CanRespawn(bData.monumentName, 1, True) == 1)
                                {
                                    SpawnBots(bData.monumentName, AllProfiles[bData.monumentName], "biome", null, spawnList[spawnPos], -1);
                                    if (bData.profile.Announce_Spawn && bData.profile.Announcement_Text != String.Empty)
                                        PrintToChat(bData.profile.Announcement_Text);
                                }
                            }
                        });
                    }
                    return;
                }
                int num = spawnsData.CustomSpawnLocations[bData.monumentName].Count;
                if (CanRespawn(bData.monumentName, 1, False) == 1)
                {
                    timer.Once(bData.profile.Respawn_Timer, () =>
                    {
                        int spawnnum = 0;
                        if (AllProfiles.ContainsKey(bData.monumentName))
                        {
                            if (CanRespawn(bData.monumentName, 1, True) == 1)
                            {
                                if (num > 0 && bData.profile.UseCustomSpawns)
                                {
                                    if (!bData.profile.ChangeCustomSpawnOnDeath)
                                        spawnnum = bData.CustomSpawnNum;
                                    else
                                        spawnnum = GetNextSpawn(bData.monumentName, bData.profile);
                                }
                                SpawnBots(bData.monumentName, AllProfiles[bData.monumentName], null, null, new Vector3(), spawnnum);
                                if (bData.profile.Announce_Spawn && bData.profile.Announcement_Text != String.Empty)
                                    PrintToChat(bData.profile.Announcement_Text);
                            }
                            else if (bData.profile.UseCustomSpawns && !bData.profile.ChangeCustomSpawnOnDeath)
                                availableStatSpawns[bData.monumentName].Add(bData.CustomSpawnNum);
                        }
                    }); 
                }
                else if (bData.profile.UseCustomSpawns && !bData.profile.ChangeCustomSpawnOnDeath)
                    availableStatSpawns[bData.monumentName].Add(bData.CustomSpawnNum);
                //UnityEngine.Object.Destroy(npc.GetComponent<BotData>());
            }
        }

        public static readonly FieldInfo AllScientists = typeof(Scientist).GetField("AllScientists", (BindingFlags.Static | BindingFlags.Public)); //NRE AskQuestion workaround
        void OnEntitySpawned(Scientist sci)
        {
            if (loaded && sci != null)
                AllScientists.SetValue(sci, new HashSet<Scientist>());//NRE AskQuestion workaround 
        }

        void OnEntitySpawned(DroppedItemContainer container)
        {
            NextTick(() =>
            {
                if (!loaded || container == null || container.IsDestroyed)
                    return;

                if (container.playerSteamID == 0) return;

                if (configData.Global.Remove_BackPacks_Percent >= GetRand(1, 101))
                    if (DeadNPCPlayerIds.Contains(container.playerSteamID))
                    {
                        container.Kill();
                        DeadNPCPlayerIds.Remove(container.playerSteamID);
                        return;
                    }
            });
        }

        void OnEntitySpawned(SupplySignal signal)
        {
            timer.Once(2.3f, () =>
            {
                if (!loaded || signal != null)
                    SmokeGrenades.Add(new Vector3(signal.transform.position.x, 0, signal.transform.position.z));
            });
        }

        void OnEntitySpawned(SupplyDrop drop)
        {
            if (!loaded || (!drop.name.Contains("supply_drop") && !drop.name.Contains("sleigh/presentdrop")))
                return;

            if (!configData.Global.Supply_Enabled)
            {
                foreach (var location in SmokeGrenades.Where(location => Vector3.Distance(location, new Vector3(drop.transform.position.x, 0, drop.transform.position.z)) < 35f))
                {
                    SmokeGrenades.Remove(location);
                    return;
                }
            }
            if (AllProfiles.ContainsKey("AirDrop"))
            {
                var prof = AllProfiles["AirDrop"];
                if (prof.AutoSpawn == True && prof.Day_Time_Spawn_Amount > 0)
                {
                    var profile = AllProfiles["AirDrop"];
                    if (profile.Announce_Spawn && profile.Announcement_Text != String.Empty)
                        PrintToChat(profile.Announcement_Text);

                    timer.Repeat(1f, profile.Day_Time_Spawn_Amount, () =>
                    {
                        profile.Location = drop.transform.position;
                        SpawnBots("AirDrop", profile, "AirDrop", null, new Vector3(), -1);
                    });
                }
            }
        }

        void OnEntitySpawned(NPCPlayerCorpse corpse)
        {
            if (!loaded || corpse == null)
                return;

            Inv botInv = new Inv();
            ulong id = corpse.playerSteamID;
            timer.Once(0.1f, () =>
            {
                if (corpse == null || !botInventories.ContainsKey(id))
                    return;

                botInv = botInventories[id];
                DataProfile profile = botInv.profile;

                timer.Once(profile.Corpse_Duration, () =>
                {
                    if (corpse != null && !corpse.IsDestroyed)
                        corpse?.Kill();
                });
                timer.Once(2, () => corpse?.ResetRemovalTime(profile.Corpse_Duration));

                List<Item> toDestroy = new List<Item>();
                foreach (var item in corpse.containers[0].itemList)
                {
                    if (item.ToString().ToLower().Contains("keycard") && configData.Global.Remove_KeyCard) 
                        toDestroy.Add(item);
                }
                foreach (var item in toDestroy)
                    item.Remove();
                if (!(profile.Allow_Rust_Loot_Percent >= GetRand(1, 101)))
                {
                    corpse.containers[0].Clear(); 
                    corpse.containers[1].Clear();
                    corpse.containers[2].Clear();
                }

                Item playerSkull = ItemManager.CreateByName("skull.human", 1);
                playerSkull.name = string.Concat($"Skull of {corpse.playerName}");
                ItemAmount SkullInfo = new ItemAmount() { itemDef = playerSkull.info, amount = 1, startAmount = 1 };
                var dispenser = corpse.GetComponent<ResourceDispenser>();
                if (dispenser != null)
                {
                    dispenser.containedItems.Add(SkullInfo);
                    dispenser.Initialize();
                }

                for (int i = 0; i < botInv.inventory.Length; i++)
                {
                    foreach (var item in botInv.inventory[i])
                    {
                        var giveItem = ItemManager.CreateByItemID(item.ID, item.amount, item.skinID); 
                        if (!giveItem.MoveToContainer(corpse.containers[i], -1, True))
                            giveItem.Remove();
                    }
                }
                timer.Once(5f, () => 
                {
                    botInventories.Remove(id);
                });
                if (profile.Wipe_Belt_Percent >= GetRand(1, 101))
                    corpse.containers[2].Clear();
                if (profile.Wipe_Clothing_Percent >= GetRand(1, 101))
                    corpse.containers[1].Clear();
                ItemManager.DoRemoves();
            });
        }
        #endregion

        #region WeaponSwitching
        void SelectWeapon(NPCPlayerApex npcPlayer)
        {
            if (npcPlayer == null)
                return;
            Item active = npcPlayer.GetActiveItem();
            var bData = npcPlayer.GetComponent<BotData>();
            if (bData == null)
                return;
            Item targetItem = null;
            List<Item> rangeToUse = new List<Item>();

            if (active == null)
            {
                rangeToUse = bData.Weapons[0];
                targetItem = rangeToUse[random.Next(rangeToUse.Count)];
                foreach (var item in npcPlayer.inventory.containerBelt.itemList.Where(item => item.info.itemid == targetItem.info.itemid))
                    targetItem = item;
            }
            else if (npcPlayer.AttackTarget == null)
            {
                ToggleAggro(npcPlayer, Convert.ToByte(!bData.profile.Peace_Keeper), bData.profile.Aggro_Range);
                if (bData.profile.AlwaysUseLights || IsNight)
                {
                    foreach (var item in npcPlayer.inventory.containerBelt.itemList.Where(item => item.GetHeldEntity() is TorchWeapon))
                        targetItem = item;

                    if (LightEquipped(npcPlayer) != null)
                        targetItem = LightEquipped(npcPlayer);
                }
            }
            else
            {
                float distance = Vector3.Distance(npcPlayer.transform.position, npcPlayer.AttackTarget.transform.position);
                if (bData.Weapons[0].Count == 1 || bData.enemyDistance == GetRange(distance))
                    targetItem = active;
                else
                {
                    bData.enemyDistance = GetRange(distance);
                    rangeToUse = bData.Weapons[bData.enemyDistance];
                    if (!rangeToUse.Any())
                    {
                        if (active.GetHeldEntity() as BaseMelee != null && GetRange(distance) > 1)
                            foreach (var weapon in bData.Weapons[0])
                                if (weapon != active)
                                    targetItem = weapon;
                    }
                    else
                        targetItem = rangeToUse[random.Next(rangeToUse.Count)];
                }
            }
            if (targetItem != null)
                UpdateActiveItem(npcPlayer, targetItem); 
            else
                UpdateActiveItem(npcPlayer, active);
        }

        int GetRange(float distance)
        {
            if (distance < 2f) return 1;
            if (distance < 10f) return 2;
            if (distance < 40f) return 3;
            return 4;
        }

        void UpdateActiveItem(NPCPlayerApex npcPlayer, Item item)
        {
            Item activeItem1 = npcPlayer.GetActiveItem();
            HeldEntity heldEntity;
            HeldEntity heldEntity1;
            if (activeItem1 != item)
            {
                npcPlayer.svActiveItemID = 0U;
                if (activeItem1 != null)
                {
                    heldEntity = activeItem1.GetHeldEntity() as HeldEntity;
                    if (heldEntity != null)
                        heldEntity.SetHeld(False);
                }
                npcPlayer.svActiveItemID = item.uid;
                npcPlayer.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                npcPlayer.inventory.UpdatedVisibleHolsteredItems();
                npcPlayer.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                SetRange(npcPlayer, item);
                heldEntity1 = npcPlayer.GetActiveItem()?.GetHeldEntity() as HeldEntity;
                if (heldEntity1 != null)
                    heldEntity1.SetHeld(True);
            }
            else
            {
                var lights = npcPlayer.GetComponent<BotData>().profile.AlwaysUseLights;
                heldEntity1 = npcPlayer.GetActiveItem()?.GetHeldEntity() as HeldEntity;
                if (heldEntity1 != null)
                    heldEntity1.SetLightsOn(lights ? True : IsNight);
                HeadLampToggle(npcPlayer, lights ? True : IsNight);
            }
        }

        Item LightEquipped(NPCPlayerApex npcPlayer)
        {
            foreach (var item in npcPlayer.inventory.containerBelt.itemList.Where(item => item.GetHeldEntity() is BaseProjectile && item.contents != null))
                foreach (var mod in (item.contents.itemList).Where(mod => mod.GetHeldEntity() as ProjectileWeaponMod != null && mod.info.name == "flashlightmod.item"))
                    return item;
            return null;
        }

        void SetRange(NPCPlayerApex npcPlayer, Item item)
        {
            var bData = npcPlayer.GetComponent<BotData>();
            var weapon = npcPlayer.GetHeldEntity() as AttackEntity;
            if (bData != null && weapon != null)
                weapon.effectiveRange = bData.Weapons[1].Contains(item) ? 2 : 350;
        }

        void HeadLampToggle(NPCPlayerApex npcPlayer, bool On)
        {
            foreach (var item in npcPlayer.inventory.containerWear.itemList)
                if (item.info.shortname.Equals("hat.miner") || item.info.shortname.Equals("hat.candle"))
                {
                    if (OnOff(item, On,npcPlayer))
                    {
                        item.SwitchOnOff(On);
                        npcPlayer.inventory.ServerUpdate(0f);
                        break;
                    }
                }
        }

        bool OnOff(Item item, bool On, NPCPlayer npc) => (On && !item.IsOn()) || (!On && item.IsOn());

        #endregion

        List<ulong> humCheck = new List<ulong>();
        void OnEntitySpawned(BasePlayer player)
        {
            if (player?.net?.connection == null)
                humCheck = (List<ulong>)HumanNPC?.Call("HumanNPCs");
        }

        #region onnpHooks
        object OnNpcResume(NPCPlayerApex npcPlayer)
        {
            var bData = npcPlayer.GetComponent<BotData>();
            return (bData != null && (bData.inAir || bData.stationary)) ? True : Null;
        }

        object OnNpcDestinationSet(NPCPlayerApex npcPlayer)
        {
            if (npcPlayer == null || !npcPlayer.GetNavAgent.isOnNavMesh)
                return True;

            var bData = npcPlayer.GetComponent<BotData>();
            return (bData != null && (bData.goingHome)) ? True : Null;
        }

        object OnNpcTarget(IHTNAgent npc, NPCPlayerApex npcPlayer)
        {
            if (npcPlayer != null && NPCPlayers.ContainsKey(npcPlayer.userID) && !configData.Global.HTNs_Attack_BotSpawn)
                return True;
            return null;
        }

        object OnNpcTarget(NPCPlayerApex npcPlayer, BaseEntity entity)
        {
            if (npcPlayer == null || entity == null)
                return null;

            bool attackerIsMine = NPCPlayers.ContainsKey(npcPlayer.userID);

            NPCPlayer botVictim = entity as NPCPlayer;
            if (botVictim != null)
            {
                bool vicIsMine = False;
                vicIsMine = NPCPlayers.ContainsKey(botVictim.userID);
                if (npcPlayer == botVictim)
                    return null;

                if (vicIsMine && !attackerIsMine && !configData.Global.NPCs_Attack_BotSpawn)//stop oustideNPCs attacking BotSpawn bots    
                    return True;

                if (!attackerIsMine)
                    return null;

                if (vicIsMine)
                {
                    var bData = npcPlayer.GetComponent<BotData>();
                    if (!bData.profile.Attacks_Other_Profiles || bData.monumentName == botVictim.GetComponent<BotData>().monumentName)
                        return True;

                    ForceMemory(npcPlayer, botVictim);
                }
                if (!vicIsMine && !configData.Global.BotSpawn_Attacks_NPCs)//stop BotSpawn bots attacking outsideNPCs
                    return True;
            }

            if (!attackerIsMine)
                return null;

            BasePlayer victim = entity as BasePlayer;
            if (victim != null)
            {
                if (configData.Global.Ignore_HumanNPC && humCheck != null && humCheck.Contains(victim.userID))
                    return True;

                var bData = npcPlayer.GetComponent<BotData>();
                bData.goingHome = False;

                var active = npcPlayer?.GetActiveItem()?.GetHeldEntity() as HeldEntity;
                if (active == null)//freshspawn catch, pre weapon draw.  
                    return null;
                if (bData.profile.Peace_Keeper)
                {
                    var held = victim.GetHeldEntity();

                    var heldWeapon = held as BaseProjectile;
                    var heldFlame = held as FlameThrower;

                    if (heldWeapon != null || heldFlame != null)
                        if (!bData.AggroPlayers.Contains(victim.userID))
                            bData.AggroPlayers.Add(victim.userID);

                    if ((heldWeapon == null && heldFlame == null) || (victim.svActiveItemID == 0u))
                    {
                        if (bData.AggroPlayers.Contains(victim.userID) && !bData.coolDownPlayers.Contains(victim.userID))
                        {
                            bData.coolDownPlayers.Add(victim.userID);
                            timer.Once(bData.profile.Peace_Keeper_Cool_Down, () =>
                            {
                                if (bData.AggroPlayers.Contains(victim.userID))
                                {
                                    bData.AggroPlayers.Remove(victim.userID);
                                    bData.coolDownPlayers.Remove(victim.userID);
                                }
                            });
                        }
                        if (!bData.AggroPlayers.Contains(victim.userID))
                            return True;
                    }
                }
                bool OnNav = npcPlayer.GetNavAgent.isOnNavMesh;
                if (OnNav)
                {
                    var distance = Vector3.Distance(npcPlayer.transform.position, victim.transform.position);
                    if (distance < 50)
                    {
                        var heightDifference = victim.transform.position.y - npcPlayer.transform.position.y;
                        if (heightDifference > 5)
                            npcPlayer.SetDestination(npcPlayer.transform.position - (Quaternion.Euler(npcPlayer.serverInput.current.aimAngles) * Vector3.forward * 2));
                    }
                }

                if (!bData.stationary && !bData.inAir && npcPlayer is NPCMurderer)
                {
                    var distance = Vector3.Distance(npcPlayer.transform.position, victim.transform.position);
                    if (npcPlayer.lastAttacker != victim && distance > npcPlayer.Stats.AggressionRange && distance > npcPlayer.Stats.DeaggroRange)
                        return True;

                    var held = npcPlayer.GetHeldEntity();
                    if (held == null)
                        return null;
                    if (held as BaseProjectile == null && OnNav)
                    {
                        NavMeshPath pathToEntity = new NavMeshPath();
                        npcPlayer.AiContext.AIAgent.GetNavAgent.CalculatePath(victim.ServerPosition, pathToEntity);
                        if (npcPlayer.lastAttacker != null && victim == npcPlayer.lastAttacker && pathToEntity.status == NavMeshPathStatus.PathInvalid && !bData.fleeing)
                        {
                            var heightDifference = victim.transform.position.y - npcPlayer.transform.position.y;
                            if (heightDifference > 1 && distance < 50)
                            {
                                bData.fleeing = True;
                                timer.Once(10f, () =>
                                {
                                    if (npcPlayer != null)
                                        bData.fleeing = False;
                                });
                                WipeMemory(npcPlayer);
                                return True;
                            }
                        }
                        if (pathToEntity.status != NavMeshPathStatus.PathInvalid && bData.fleeing)
                        {
                            ForceMemory(npcPlayer, victim);
                            return null;
                        }
                    }
                    ForceMemory(npcPlayer, victim);
                }

                bData.goingHome = False;

                if (victim.IsSleeping() && configData.Global.Ignore_Sleepers)
                    return True;
                if (npcPlayer.AttackTarget == null)
                {
                    ToggleAggro(npcPlayer, 1, npcPlayer.Stats.AggressionRange);
                    npcPlayer.AttackTarget = victim;
                    npcPlayer.lastAttacker = victim;
                }
            }

            return (entity.name.Contains("agents/") || (entity is HTNPlayer && configData.Global.Ignore_HTN))
                ? True
                : Null;
        }

        object OnNpcTarget(BaseNpc npc, NPCPlayer npcPlayer)//stops animals targeting bots
        {
            return (npcPlayer != null && NPCPlayers.ContainsKey(npcPlayer.userID) && configData.Global.Animal_Safe) ? True : Null;
        }

        object OnNpcStopMoving(NPCPlayerApex npcPlayer)
        {
            if (npcPlayer == null)
                return null;
            return NPCPlayers.ContainsKey(npcPlayer.userID) ? True : Null;
        }
        #endregion

        void WipeMemory(NPCPlayerApex npc)
        {
            npc.lastDealtDamageTime = Time.time - 21;
            npc.lastAttackedTime = Time.time - 31;
            npc.AttackTarget = null;
            npc.lastAttacker = null;
            npc.SetFact(NPCPlayerApex.Facts.HasEnemy, 0, True, True);
        }

        void ForceMemory(NPCPlayerApex npc, BasePlayer victim)
        {
            npc.SetFact(NPCPlayerApex.Facts.HasEnemy, 1, True, True);
            Vector3 vector3;
            float single, single1, single2;
            Rust.Ai.BestPlayerDirection.Evaluate(npc, victim.ServerPosition, out vector3, out single);
            Rust.Ai.BestPlayerDistance.Evaluate(npc, victim.ServerPosition, out single1, out single2);
            var info = new Rust.Ai.Memory.ExtendedInfo();
            npc.AiContext.Memory.Update(victim, victim.ServerPosition, 1, vector3, single, single1, 1, True, 1f, out info);
        }

        #region SetUpLocations
        public Dictionary<string, ConfigProfile> GotMonuments = new Dictionary<string, ConfigProfile>();
        public Dictionary<string, GameObject> mons = new Dictionary<string, GameObject>();

        void CheckMonuments(bool add)
        {
            foreach (var monumentInfo in TerrainMeta.Path.Monuments.OrderBy(x => x.displayPhrase.english))
            {
                var displayPhrase = monumentInfo.displayPhrase.english.Replace("\n", String.Empty);
                if (displayPhrase.Contains("Oil Rig") || displayPhrase.Contains("Water Well"))
                    continue;
                GameObject gobject = monumentInfo.gameObject;
                var pos = monumentInfo.gameObject.transform.position;
                var rot = monumentInfo.gameObject.transform.transform.eulerAngles.y;

                int counter = 0;
                if (displayPhrase != String.Empty)
                {
                    if (add)
                    {
                        foreach (var entry in AllProfiles.Where(x => x.Key.Contains(displayPhrase) && x.Key.Length == displayPhrase.Length + 2))
                            counter++;
                        if (counter < 10)
                        {
                            mons.Add($"{displayPhrase} {counter}", gobject);
                            AddProfile($"{displayPhrase} {counter}", null, pos, rot);
                        }
                    }
                    else
                    {
                        foreach (var entry in GotMonuments.Where(x => x.Key.Contains(displayPhrase) && x.Key.Length == displayPhrase.Length + 2))
                            counter++;
                        if (counter < 10)
                            GotMonuments.Add($"{displayPhrase} {counter}", new ConfigProfile());
                    }
                }
            }
        }

        private void SetupProfiles()
        {
            CheckMonuments(True);
            int BiomeCounter = 1;
            foreach (var entry in defaultData.Biomes)
            {
                ConfigProfile prof = JsonConvert.DeserializeObject<ConfigProfile>(JsonConvert.SerializeObject(entry.Value));
                AddProfile(entry.Key, prof, new Vector3(), 0f);
                if (entry.Value.AutoSpawn)
                    GenerateSpawnPoints(entry.Key, Mathf.Max(prof.Night_Time_Spawn_Amount, prof.Day_Time_Spawn_Amount), timers[entry.Key], BiomeCounter);
                BiomeCounter *= 2;
            }

            DataProfile Airdrop = JsonConvert.DeserializeObject<DataProfile>(JsonConvert.SerializeObject(defaultData.Events.AirDrop));
            AllProfiles.Add("AirDrop", Airdrop);
            foreach (var profile in storedData.DataProfiles)
                AddData(profile.Key, profile.Value);

            SaveData();
            SetupSpawnsFile();
            foreach (var profile in AllProfiles)
            {
                popinfo.Add(profile.Key, new PopInfo());
                popinfo[profile.Key].spawnTracker = -1;
                if (timers.ContainsKey(profile.Key) || profile.Key.Contains("AirDrop"))
                    continue;
                if (profile.Value.Kit.Count > 0 && Kits == null)
                {
                    PrintWarning(lang.GetMessage("nokits", this), profile.Key);
                    continue;
                }
                int num = spawnsData.CustomSpawnLocations[profile.Key].Count;
                if (profile.Value.AutoSpawn == True && (profile.Value.Day_Time_Spawn_Amount > 0 || profile.Value.Night_Time_Spawn_Amount > 0))
                {
                    for (int i = 0; i < num; i++)
                        availableStatSpawns[profile.Key].Add(i);

                    int target = TargetAmount(profile.Value);
                    if (target > 0)
                    {
                        int amount = CanRespawn(profile.Key, target, False);
                        if (amount > 0)
                            timer.Repeat(0.5f, amount, () =>
                            {
                                if (AllProfiles.Contains(profile) && CanRespawn(profile.Key, 1, True) == 1)
                                {
                                    int point = GetNextSpawn(profile.Key, profile.Value);
                                    SpawnBots(profile.Key, AllProfiles[profile.Key], null, null, new Vector3(), point);
                                    if (point != -1)
                                        availableStatSpawns[profile.Key].Remove(point);
                                }
                            });
                    }
                }
            }
        }

        void AddProfile(string name, ConfigProfile monument, Vector3 pos, float rotation)//bring config data into live data 
        {
            if (monument == null && defaultData.Monuments.ContainsKey(name))
                monument = defaultData.Monuments[name];
            else if (monument == null)
                monument = new ConfigProfile();

            var toAdd = JsonConvert.SerializeObject(monument);
            DataProfile toAddDone = JsonConvert.DeserializeObject<DataProfile>(toAdd);
            if (AllProfiles.ContainsKey(name))
                return;

            AllProfiles.Add(name, toAddDone);
            AllProfiles[name].Location = pos;

            foreach (var custom in storedData.DataProfiles.Where(custom => custom.Value.Parent_Monument == name && storedData.MigrationDataDoNotEdit.ContainsKey(custom.Key)))
            {
                var path = storedData.MigrationDataDoNotEdit[custom.Key];
                if (path.ParentMonument == new Vector3())
                {
                    path.ParentMonument = pos;
                    path.Offset = mons[name].transform.InverseTransformPoint(custom.Value.Location);
                }
            }
            //SaveData();
        }

        void AddData(string name, DataProfile profile) 
        {
            if (!storedData.MigrationDataDoNotEdit.ContainsKey(name))
                storedData.MigrationDataDoNotEdit.Add(name, new ProfileRelocation());

            if (profile.Parent_Monument != String.Empty)
            {
                var path = storedData.MigrationDataDoNotEdit[name];
                if (AllProfiles.ContainsKey(profile.Parent_Monument) && !timers.ContainsKey(profile.Parent_Monument))
                {
                    if (path.ParentMonument == Vector3.zero)
                    {
                        Puts($"Saving new offset for Parent Monument for {name}");
                        path.ParentMonument = AllProfiles[profile.Parent_Monument].Location;
                        path.Offset = mons[profile.Parent_Monument].transform.InverseTransformPoint(profile.Location);
                    }

                    if (path.ParentMonument != AllProfiles[profile.Parent_Monument].Location)
                    {
                        bool userChanged = False;
                        foreach (var monument in AllProfiles)
                            if (monument.Value.Location == AllProfiles[profile.Parent_Monument].Location)
                            {
                                userChanged = True;
                                break;
                            }

                        if (userChanged)
                        {
                            Puts($"Parent_Monument change detected - Saving {name} location relative to {profile.Parent_Monument}");
                            path.ParentMonument = AllProfiles[profile.Parent_Monument].Location;
                            profile.Location = mons[profile.Parent_Monument].transform.TransformPoint(path.Offset);
                        }
                        else
                        {
                            Puts($"Map seed change detected - Updating {name} location relative to new {profile.Parent_Monument}");
                            path.ParentMonument = AllProfiles[profile.Parent_Monument].Location;
                            profile.Location = mons[profile.Parent_Monument].transform.TransformPoint(path.Offset);
                        }
                    }
                }
                else if (profile.AutoSpawn == True)
                {
                    Puts($"Parent monument {profile.Parent_Monument} does not exist for custom profile {name}");
                    return;
                }
            }
            SaveData();
            AllProfiles.Add(name, profile);
        }

        void SetupSpawnsFile()
        {
            bool flag = False, flag2 = False;
            foreach (var entry in AllProfiles.Where(entry => !timers.ContainsKey(entry.Key) && entry.Key != "AirDrop"))
            {
                if (!spawnsData.CustomSpawnLocations.ContainsKey(entry.Key))
                {
                    spawnsData.CustomSpawnLocations.Add(entry.Key, new List<string>());
                    flag = True;
                }
                if (!availableStatSpawns.ContainsKey(entry.Key))
                    availableStatSpawns.Add(entry.Key, new List<int>());
                if (entry.Value.UseCustomSpawns)
                {
                    if (spawnsData.CustomSpawnLocations[entry.Key].Count < entry.Value.Day_Time_Spawn_Amount)
                    {
                        PrintWarning(lang.GetMessage("notenoughspawns", this), entry.Key);
                        entry.Value.Day_Time_Spawn_Amount = spawnsData.CustomSpawnLocations[entry.Key].Count;
                        flag2 = True;
                    }
                    if (spawnsData.CustomSpawnLocations[entry.Key].Count < entry.Value.Night_Time_Spawn_Amount)
                    {
                        PrintWarning(lang.GetMessage("notenoughspawns", this), entry.Key);
                        entry.Value.Night_Time_Spawn_Amount = spawnsData.CustomSpawnLocations[entry.Key].Count;
                        flag2 = True;
                    }
                    else if (spawnsData.CustomSpawnLocations[entry.Key].Count == 0)
                        PrintWarning(lang.GetMessage("nospawns", this), entry.Key);
                }
            }
            if (flag2)
                SaveData();
            else if (flag)
                SaveSpawns();
        }

        #endregion

        #region Commands
        [ConsoleCommand("bot.count")]
        void CmdBotCount()
        {
            string msg = (NPCPlayers.Count == 1) ? "numberOfBot" : "numberOfBots";
            PrintWarning(lang.GetMessage(msg, this), NPCPlayers.Count);
        }

        [ConsoleCommand("bots.count")]
        void CmdBotsCount()
        {
            var records = BotSpawnBots();
            if (records.Count == 0)
            {
                PrintWarning("There are no spawned npcs");
                return;
            }
            bool none = True;
            foreach (var entry in records)
                if (entry.Value.Count > 0)
                    none = False;
            if (none)
            {
                PrintWarning("There are no spawned npcs");
                return;
            }
            foreach (var entry in BotSpawnBots().Where(x => AllProfiles[x.Key].AutoSpawn == True))
                PrintWarning(entry.Key + " - " + entry.Value.Count);
        }

        [ConsoleCommand("botspawn")]
        private void CmdBotSpawn(ConsoleSystem.Arg arg)
        {
            var player = arg?.Connection?.player as BasePlayer;
            if ((player != null && player?.net?.connection.authLevel < 2) || arg?.Args?.Length != 2)
                return;
            if (arg.Args[0] == "spawn")
            {
                var profile = arg.Args[1];
                foreach (var entry in AllProfiles.Where(entry => entry.Key.ToLower() == profile.ToLower()))
                {
                    if (timers.ContainsKey(entry.Key) || entry.Key == "AirDrop")
                    {
                        PrintWarning(lang.GetMessage("Title", this) + lang.GetMessage("norespawn", this));
                        continue;
                    }
                    var loc = entry.Value.UseCustomSpawns && spawnsData.CustomSpawnLocations[entry.Key].Count > 0 ? Vector3.zero : entry.Value.Location;
                    if (TargetAmount(AllProfiles[entry.Key]) == 0)
                    {
                        PrintWarning(lang.GetMessage("Title", this) + lang.GetMessage("targetzero", this), entry.Key);
                        return;
                    }
                    timer.Repeat(1f, TargetAmount(entry.Value), () => DeployNpcs(loc, entry.Key, entry.Value, null, GetNextSpawn(entry.Key, entry.Value)));
                    PrintWarning(lang.GetMessage("Title", this) + lang.GetMessage("deployed", this), entry.Key, entry.Value.Location);
                    return;
                }
                PrintWarning(lang.GetMessage("Title", this) + lang.GetMessage("noprofile", this));
            }
            if (arg.Args[0] == "kill")
            {
                var profile = arg.Args[1];
                BotData bData = null;
                List<NPCPlayerApex> killList = new List<NPCPlayerApex>();
                bool found = False;
                foreach (var entry in AllProfiles.Where(entry => entry.Key.ToLower() == profile.ToLower()))
                {
                    foreach (var npc in NPCPlayers)
                    {
                        bData = npc.Value.GetComponent<BotData>();
                        if (bData.monumentName.ToLower() == entry.Key.ToLower() && !bData.respawn)
                        {
                            found = True;
                            killList.Add(npc.Value);
                        }
                    }
                    if (found)
                    {
                        NextTick(() =>
                        {
                            PrintWarning(lang.GetMessage("Title", this) + lang.GetMessage("killed", this), entry.Key);
                            foreach (var npc in killList.ToList())
                                npc.Kill();
                        });
                    }
                    else
                        PrintWarning(lang.GetMessage("Title", this) + lang.GetMessage("nonpcs", this));
                    return;
                }
                PrintWarning(lang.GetMessage("Title", this) + lang.GetMessage("noprofile", this));
            }
        }

        public NPCPlayerApex GetNPCPlayer(BasePlayer player)
        {
            Vector3 start = player.eyes.position;
            Ray ray = new Ray(start, Quaternion.Euler(player.eyes.rotation.eulerAngles) * Vector3.forward);
            var hits = Physics.RaycastAll(ray);
            foreach (var hit in hits)
            {
                var npc = hit.collider?.GetComponentInParent<NPCPlayerApex>();
                if (npc?.GetComponent<BotData>() != null && hit.distance < 2f)
                    return npc;
            }
            return null;
        }

        string TitleText => "<color=orange>" + lang.GetMessage("Title", this) + "</color>";

        [ConsoleCommand("botspawn.addspawn")]
        private void botspawnAddSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Player() == null)
                return;
            Botspawn(arg.Player(), "botspawn", new string[] { "addspawn" });
        }

        [ConsoleCommand("botspawn.removespawn")]
        private void botspawnRemoveSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Player() == null)
                return;
            Botspawn(arg.Player(), "botspawn", new string[] { "removespawn" });
        }

        [ConsoleCommand("botspawn.info")]
        private void botspawnInfo(ConsoleSystem.Arg arg)
        {
            if (arg.Player() == null)
                return;
            Botspawn(arg.Player(), "botspawn", new string[] { "info" });
        }

        [ChatCommand("botspawn")]
        void Botspawn(BasePlayer player, string command, string[] args)
        {
            if (!HasPermission(player.UserIDString, permAllowed) && !IsAuth(player))
                return;
            string pn = string.Empty;
            var sp = spawnsData.CustomSpawnLocations;

            if (args != null && args.Length == 1)
            {
                if (args[0] == "info")
                {
                    var npc = GetNPCPlayer(player);
                    if (npc == null)
                        SendReply(player, TitleText + lang.GetMessage("nonpc", this));
                    else
                        SendReply(player, TitleText + "NPC from profile - " + npc.GetComponent<BotData>().monumentName);
                    return;
                }
                if (args[0] == "list")
                {
                    var outMsg = lang.GetMessage("ListTitle", this);
                    foreach (var profile in storedData.DataProfiles)
                        outMsg += $"\n{profile.Key}";
                    PrintToChat(player, outMsg);
                    return;
                }

                if (editing.ContainsKey(player.userID) && AllProfiles.ContainsKey(editing[player.userID]))
                    pn = editing[player.userID];
                else
                {
                    SendReply(player, TitleText + lang.GetMessage("notediting", this));
                    return;
                }
                if (args[0] == "addspawn")
                {
                    var loc = player.transform.position;
                    var rot = player.eyes.rotation;
                    if (!HasNav(loc) && !AllProfiles[pn].Stationary)
                        SendReply(player, TitleText + lang.GetMessage("noNavHere", this));
                    sp[pn].Add($"{loc.x},{loc.y},{loc.z},{rot.x},{rot.y},{rot.z},{rot.w}");
                    SaveSpawns();
                    ShowSpawn(player, loc, sp[pn].Count, 10f);
                    SendReply(player, TitleText + lang.GetMessage("addedspawn", this), sp[pn].Count, pn);
                    return;
                }
                if (args[0] == "removespawn")
                {
                    if (sp[pn].Count > 0)
                    {
                        sp[pn].RemoveAt(sp[pn].Count - 1);
                        SaveSpawns();
                        SendReply(player, TitleText + lang.GetMessage("removedspawn", this), pn, sp[pn].Count);
                        return;
                    }
                    else
                    {
                        SendReply(player, TitleText + lang.GetMessage("nospawns", this), pn, sp[pn].Count);
                        return;
                    }
                }
                SendReply(player, TitleText + lang.GetMessage("error", this));

            }
            else if (args != null && args.Length == 2)
            {
                var name = args[1];
                if (args[0] == "reload")
                {
                    if (AllProfiles.ContainsKey(name))
                    {
                        ReloadData(name);
                        foreach (var npc in NPCPlayers.ToList())
                        {
                            var bData = npc.Value.GetComponent<BotData>();
                            if (bData.monumentName == name)
                            {
                                bData.profile = AllProfiles[name].Clone();
                                bData.profile.Respawn_Timer = 0;
                                npc.Value.Kill();  
                            }
                        }
                        SendReply(player, TitleText + lang.GetMessage("reloaded", this));
                        return;
                    }
                    SendReply(player, TitleText + lang.GetMessage("noprofile", this));
                }
                if (args[0] == "show")
                {
                    int amount;
                    if (int.TryParse(args[1], out amount))
                    {
                        string entry = null;
                        if (editing.ContainsKey(player.userID))
                            entry = editing[player.userID];

                        if (string.IsNullOrEmpty(entry))
                        {
                            SendReply(player, TitleText + lang.GetMessage("notediting", this));
                            return;
                        }

                        if (AllProfiles.ContainsKey(entry) && !timers.ContainsKey(entry) || entry != "AirDrop")
                        {
                            if (editing.ContainsKey(player.userID))
                                editing[player.userID] = entry;
                            else
                                editing.Add(player.userID, entry);
                            var path = spawnsData.CustomSpawnLocations[entry];
                            for (int i = 0; i < path.Count; i++)
                                ShowSpawn(player, s2v(path[i]), i, amount);
                            return;
                        }
                    }
                    else
                        SendReply(player, TitleText + lang.GetMessage("showduration", this), name);
                }
                if (args[0] == "edit")
                {
                    if (!spawnsData.CustomSpawnLocations.ContainsKey(name))
                    {
                        SendReply(player, TitleText + lang.GetMessage("noprofile", this));
                        return;
                    }
                    var path = spawnsData.CustomSpawnLocations[name];
                    if (AllProfiles.ContainsKey(name) && !timers.ContainsKey(name) || name != "AirDrop")
                    {
                        if (editing.ContainsKey(player.userID))
                            editing[player.userID] = name;
                        else
                            editing.Add(player.userID, name);

                        for (int i = 0; i < path.Count; i++)
                            ShowSpawn(player, s2v(path[i]), i, 10f); 

                        SendReply(player, TitleText + lang.GetMessage("editingname", this), name);
                    }
                    else
                        SendReply(player, TitleText + lang.GetMessage("noprofile", this));
                    return;
                }
                if (args[0] == "add")
                {
                    if (AllProfiles.ContainsKey(name))
                    {
                        SendReply(player, TitleText + lang.GetMessage("alreadyexists", this), name);
                        return;
                    }
                    Vector3 pos = player.transform.position;
                    var customSettings = new DataProfile()
                    {
                        AutoSpawn = False,
                        BotNames = new List<string> { String.Empty },
                        Location = pos,
                    };
                    storedData.DataProfiles.Add(name, customSettings);
                    AddData(name, customSettings);
                    SetupSpawnsFile();
                    if (editing.ContainsKey(player.userID))
                        editing[player.userID] = name;
                    else
                        editing.Add(player.userID, name);

                    SaveData();
                    SendReply(player, TitleText + lang.GetMessage("customsaved", this), player.transform.position);
                    return;
                }
                if (args[0] == "move")
                {
                    if (storedData.DataProfiles.ContainsKey(name))
                    {
                        var d = storedData.DataProfiles[name];
                        d.Location = player.transform.position;
                        if (AllProfiles.ContainsKey(d.Parent_Monument))
                            storedData.MigrationDataDoNotEdit[name].Offset = mons[d.Parent_Monument].transform.InverseTransformPoint(player.transform.position);
                        SaveData();
                        SendReply(player, TitleText + lang.GetMessage("custommoved", this), name);
                    }
                    else
                        SendReply(player, TitleText + lang.GetMessage("noprofile", this));
                    return;
                }
                if (args[0] == "remove")
                {
                    if (storedData.DataProfiles.ContainsKey(name))
                    {
                        List<NPCPlayerApex> toDestroy = new List<NPCPlayerApex>();

                        foreach (var bot in NPCPlayers)
                        {
                            if (bot.Value == null)
                                continue;
                            var bData = bot.Value.GetComponent<BotData>();
                            if (bData.monumentName == name)
                                toDestroy.Add(bot.Value);
                        }
                        NextTick(() =>
                        {
                            foreach (var killBot in toDestroy)
                                killBot.Kill();
                        });

                        AllProfiles.Remove(name);
                        storedData.DataProfiles.Remove(name);
                        storedData.MigrationDataDoNotEdit.Remove(name);
                        SaveData();
                        SendReply(player, TitleText + lang.GetMessage("customremoved", this), name);
                    }
                    else
                        SendReply(player, TitleText + lang.GetMessage("noprofile", this));
                    return;
                }
                if (args[0] == "spawn")
                {
                    var profile = args[1];
                    foreach (var entry in AllProfiles.Where(entry => entry.Key.ToLower() == profile.ToLower()))
                    {
                        if (timers.ContainsKey(entry.Key) || entry.Key == "AirDrop")
                        {
                            SendReply(player, TitleText + lang.GetMessage("norespawn", this));
                            continue;
                        }
                        var loc = entry.Value.UseCustomSpawns && spawnsData.CustomSpawnLocations[entry.Key].Count > 0 ? Vector3.zero : entry.Value.Location;
                        if (TargetAmount(AllProfiles[entry.Key]) == 0)
                        {
                            SendReply(player, lang.GetMessage("Title", this) + lang.GetMessage("targetzero", this), entry.Key);
                            return;
                        }
                        timer.Repeat(1f, TargetAmount(AllProfiles[entry.Key]), () => DeployNpcs(loc, entry.Key, entry.Value, null, GetNextSpawn(entry.Key, entry.Value)));
                        SendReply(player, TitleText + lang.GetMessage("deployed", this), entry.Key, entry.Value.Location);
                        return;
                    }
                    SendReply(player, TitleText + lang.GetMessage("noprofile", this));
                    return;
                }
                if (args[0] == "kill")
                {
                    var profile = args[1];
                    var found = False;
                    List<NPCPlayerApex> killList = new List<NPCPlayerApex>();
                    foreach (var npc in NPCPlayers)
                    {
                        var bData = npc.Value.GetComponent<BotData>();
                        if (bData.monumentName.ToLower() == profile.ToLower() && !bData.respawn)
                        {
                            found = True;
                            killList.Add(npc.Value);
                        }
                    }

                    NextTick(() =>
                    {
                        if (found)
                        {
                            SendReply(player, TitleText + lang.GetMessage("killed", this), profile);
                            foreach (var npc in killList.ToList())
                                npc.Kill();
                        }
                        else
                            SendReply(player, TitleText + lang.GetMessage("nonpcs", this));
                    });
                    return;
                }

                if (args[0] == "loadspawns" || args[0] == "removespawn" || args[0] == "movespawn")
                {
                    if (editing.ContainsKey(player.userID) && AllProfiles.ContainsKey(editing[player.userID]))
                        pn = editing[player.userID];
                    else
                    {
                        SendReply(player, TitleText + lang.GetMessage("notediting", this));
                        return;
                    }
                }

                if (args[0] == "loadspawns")
                {
                    object num1 = Spawns?.CallHook("GetSpawnsCount", name);
                    if (num1 == null || num1 is string)
                    {
                        SendReply(player, $"Spawns file with name {name} not found.");
                        return;
                    }
                    for (int i = 0; i < (int)num1; i++)
                    {
                        var loc = (Vector3)Spawns?.CallHook("GetSpawn", name, i);
                        sp[pn].Add($"{loc.x},{loc.y},{loc.z},0,0,0,0");
                    }

                    for (int i = 0; i < sp[pn].Count; i++)
                        ShowSpawn(player, s2v(sp[pn][i]), i, 10f);
                    SaveSpawns();
                    SendReply(player, TitleText + lang.GetMessage("imported", this), name, pn);
                    return;
                }
                int num = -1;
                int.TryParse(args[1], out num);
                if (num == -1)
                    return;

                if (args[0] == "removespawn")
                {
                    if (sp[pn].Count() - 1 >= num)
                    {
                        sp[pn].RemoveAt(num);
                        SaveSpawns();
                        SendReply(player, TitleText + lang.GetMessage("removednum", this), num, pn);
                        return;
                    }
                    SendReply(player, TitleText + lang.GetMessage("notthatmany", this), pn, Mathf.Max(1, num));
                    return;
                }
                if (args[0] == "movespawn")
                {
                    if (sp[pn].Count() - 1 >= num)
                    {
                        var loc = player.transform.position;
                        var rot = player.eyes.rotation;
                        sp[pn][num] = $"{loc.x},{loc.y},{loc.z},{rot.x},{rot.y},{rot.z},{rot.w}";
                        ShowSpawn(player, s2v(sp[pn][num]), sp[pn].IndexOf(sp[pn][num]), 10f);
                        SaveSpawns();
                        SendReply(player, TitleText + lang.GetMessage("movedspawn", this), num, pn);
                        return;
                    }
                    SendReply(player, TitleText + lang.GetMessage("notthatmany", this), pn, Mathf.Max(1, num));
                    return;
                }


                SendReply(player, TitleText + lang.GetMessage("error", this));
                return;
            }
            else if (args != null && args.Length == 3)
            {
                if (args[0] == "toplayer")
                {
                    var name = args[1];
                    var profile = args[2].ToLower();
                    BasePlayer target = FindPlayerByName(name);
                    Vector3 location = (CalculateGroundPos(player.transform.position));
                    var found = False;
                    if (target == null)
                    {
                        SendReply(player, TitleText + lang.GetMessage("namenotfound", this), name);
                        return;
                    }
                    foreach (var entry in AllProfiles.Where(entry => entry.Key.ToLower() == profile.ToLower()))
                    {
                        if (TargetAmount(AllProfiles[entry.Key]) == 0)
                        {
                            SendReply(player, TitleText + lang.GetMessage("targetzero", this), entry.Key);
                            return;
                        }
                        timer.Repeat(1f, TargetAmount(AllProfiles[entry.Key]), () => DeployNpcs(location, entry.Key, entry.Value, null, -1));
                        SendReply(player, TitleText + lang.GetMessage("deployed", this), entry.Key, target.displayName);
                        found = True;
                        return;
                    }
                    if (!found)
                    {
                        SendReply(player, TitleText + lang.GetMessage("noprofile", this));
                        return;
                    }
                    return;
                }
                SendReply(player, TitleText + lang.GetMessage("error", this));
            }
            else
                SendReply(player, TitleText + lang.GetMessage("error", this));
        }

        void ShowSpawn(BasePlayer player, Vector3 loc, int num, float duration) => player.SendConsoleCommand("ddraw.text", duration, Color.green, loc, $"<size=80>{num}</size>");
        #endregion

        public List<ulong> DeadNPCPlayerIds = new List<ulong>(); //to tracebackpacks
        public Dictionary<ulong, string> KitRemoveList = new Dictionary<ulong, string>();
        public List<Vector3> SmokeGrenades = new List<Vector3>();
        public Dictionary<ulong, Inv> botInventories = new Dictionary<ulong, Inv>();

        public class Inv
        {
            public DataProfile profile = new DataProfile();
            public List<InvContents>[] inventory = { new List<InvContents>(), new List<InvContents>(), new List<InvContents>() };
        }

        public class InvContents
        {
            public int ID;
            public int amount;
            public ulong skinID;
        }

        #region BotMono
        public class BotData : MonoBehaviour
        {
            public NPCPlayerApex npc;
            public List<ulong> AggroPlayers = new List<ulong>();
            public List<ulong> coolDownPlayers = new List<ulong>();
            public DataProfile profile;
            public Vector3 spawnPoint;
            public List<Item>[] Weapons = { new List<Item>(), new List<Item>(), new List<Item>(), new List<Item>(), new List<Item>() };
            public int CustomSpawnNum, enemyDistance, landingAttempts;
            public string monumentName, group; //external hook identifier 
            public bool noweapon, fleeing, hasHeadLamp, stationary, inAir, goingHome, biome, respawn;
            CapsuleCollider capcol;
            Vector3 landingDirection = Vector3.zero;

            int updateCounter;

            void Start()
            {
                npc = GetComponent<NPCPlayerApex>();
                if (npc.WaterFactor() > 0.9f)
                {
                    npc.Kill();
                    return;
                }
                if (profile.Chute && !stationary)
                {
                    inAir = True;
                    capcol = npc.GetComponent<CapsuleCollider>();
                    if (capcol != null)
                    {
                        capcol.isTrigger = True;
                        npc.GetComponent<CapsuleCollider>().radius += 2f;
                    }
                    botSpawn.ToggleAggro(npc, 1, 300f);
                }
                if (stationary || inAir)
                {
                    npc.utilityAiComponent.enabled = True;
                    npc.Stats.VisionCone = -1f;
                }
                float delay = random.Next(300, 1200);
                if (respawn)
                    InvokeRepeating("Relocate", delay, delay);
                if (!noweapon)
                    InvokeRepeating("SelectWeapon", 0, 2.99f);
            }

            void SelectWeapon() => botSpawn.SelectWeapon(npc);
            public void OnDestroy() 
            {
                botSpawn.NPCPlayers.Remove(npc.userID);
                if (botSpawn.weaponCheck.ContainsKey(npc.userID))
                {
                    botSpawn.weaponCheck[npc.userID].Destroy(); 
                    botSpawn.weaponCheck.Remove(npc.userID);
                }
                CancelInvoke("Relocate");
                CancelInvoke("SelectWeapon");
            }

            void Relocate()
            {
                if (!respawn || stationary || (profile.UseCustomSpawns == True && botSpawn.spawnsData.CustomSpawnLocations[monumentName].Count > 0))
                    return;
                if (biome)
                {
                    spawnPoint = botSpawn.spawnLists[monumentName][random.Next(botSpawn.spawnLists[monumentName].Count)];
                    return;
                }

                var randomTerrainPoint = botSpawn.TryGetSpawn(profile.Location, profile.Radius);
                if (randomTerrainPoint != new Vector3())
                    spawnPoint = randomTerrainPoint + new Vector3(0, 0.5f, 0);
            }

            private void OnTriggerEnter(Collider col)
            {
                if (!inAir)
                    return;

                if (col.ToString().Contains("ZoneManager"))
                    return;
                var rb = npc.gameObject.GetComponent<Rigidbody>();
                if (landingAttempts == 0)
                    landingDirection = npc.transform.forward;

                NavMeshHit hit;
                if (NavMesh.SamplePosition(npc.transform.position, out hit, 30, -1) || landingAttempts > 5) //NavMesh.AllAreas
                {
                    if (npc.WaterFactor() > 0.9f)
                    {
                        npc.Kill();
                        return;
                    }
                    if (capcol != null)
                    {
                        capcol.isTrigger = False;
                        npc.GetComponent<CapsuleCollider>().radius -= 2f;
                    }
                    rb.isKinematic = True;
                    rb.useGravity = False;
                    npc.gameObject.layer = 17;
                    npc.ServerPosition = hit.position;
                    npc.GetNavAgent.Warp(npc.ServerPosition);
                    botSpawn.ToggleAggro(npc, Convert.ToByte(!profile.Peace_Keeper), profile.Aggro_Range);

                    foreach (var child in npc.children.Where(child => child.name.Contains("parachute")))
                    {
                        child.SetParent(null);
                        child.Kill();
                        break;
                    }
                    SetSpawn(npc);
                    landingAttempts = 0;
                }
                else
                {
                    landingAttempts++;
                    rb.useGravity = True;
                    rb.velocity = new Vector3(landingDirection.x * 15, 11, landingDirection.z * 15);
                    rb.drag = 1f;
                }
            }
            bool done = False;
            void SetSpawn(NPCPlayerApex bot)
            {
                inAir = False;
                spawnPoint = bot.transform.position;
                bot.SpawnPosition = bot.transform.position;
                bot.Resume();
            }

            void Update()
            {
                updateCounter++;

                if (updateCounter == 50)
                {
                    updateCounter = 0;
                    if (inAir || stationary)
                    {
                        if (npc?.AttackTarget != null && npc.AttackTarget is BasePlayer)
                        {
                            if (npc.IsVisibleStanding(npc.AttackTarget.ToPlayer()) && Interface.CallHook("OnNpcTarget", npc, npc.AttackTarget) == null)
                            {
                                npc.SetAimDirection((npc.AttackTarget.transform.position - npc.GetPosition()).normalized);
                                npc.StartAttack();
                            }
                            else
                                npc.SetAimDirection(new Vector3(npc.transform.forward.x, 0, npc.transform.forward.z));
                        }
                        else
                            npc.SetAimDirection(new Vector3(npc.transform.forward.x, 0, npc.transform.forward.z));

                        goingHome = False;
                        return;
                    }

                    if (npc.GetFact(NPCPlayerApex.Facts.IsAggro) == 0 && npc.AttackTarget == null && npc.GetNavAgent.isOnNavMesh)
                    {
                        npc.CurrentBehaviour = BaseNpc.Behaviour.Wander;
                        npc.SetFact(NPCPlayerApex.Facts.Speed, (byte)NPCPlayerApex.SpeedEnum.Walk, True, True);
                        npc.TargetSpeed = 2.4f;
                        var distance = Vector3.Distance(npc.transform.position, spawnPoint);

                        if (!goingHome && distance > profile.Roam_Range || npc.WaterFactor() > 0.1f)
                            goingHome = True;
                        if (goingHome && distance > 5)
                        {
                            npc.GetNavAgent.SetDestination(spawnPoint);
                            npc.Destination = spawnPoint;
                        }
                        else
                            goingHome = False;
                    }
                }
            }
        }

        public void ToggleAggro(NPCPlayerApex npcPlayer, int hostility, float distance)
        {
            var bData = npcPlayer.GetComponent<BotData>();
            if (bData != null)
            {
                npcPlayer.Stats.AggressionRange = distance;
                npcPlayer.Stats.DeaggroRange = npcPlayer.Stats.AggressionRange + 20;
                npcPlayer.Stats.Hostility = hostility;
            }
        }
        #endregion

        #region Config
        private ConfigData configData;

        public Dictionary<ulong, NPCPlayerApex> NPCPlayers = new Dictionary<ulong, NPCPlayerApex>();
        public Dictionary<string, DataProfile> AllProfiles = new Dictionary<string, DataProfile>();

        public class Global
        {
            public int DayStartHour = 8, NightStartHour = 20;
            public bool NPCs_Attack_BotSpawn = True, HTNs_Attack_BotSpawn, BotSpawn_Attacks_NPCs = True, APC_Safe = True, Turret_Safe = True, Animal_Safe = True, Supply_Enabled;
            public int Remove_BackPacks_Percent = 100;
            public bool Remove_KeyCard = True, Ignore_HumanNPC = True, Ignore_HTN = True, Ignore_Sleepers = True, Pve_Safe = True;
            public int Max_Chute_Wind_Speed = 100, Max_Chute_Fall_Speed = 100;
        }

        public class Events
        {
            public AirDropProfile AirDrop = new AirDropProfile { };
        }

        public class BaseProfile
        {
            public bool AutoSpawn;
            public bool Murderer;
            public int Day_Time_Spawn_Amount = 5;
            public int BotHealth = 100;
            public int Corpse_Duration = 60;
            public List<string> Kit = new List<string>();
            public string BotNamePrefix = String.Empty;
            public List<string> BotNames = new List<string>();
            public int Bot_Accuracy_Percent = 40, Bot_Damage_Percent = 40;
            public bool Disable_Radio = true;
            public int Roam_Range = 40;
            public bool Peace_Keeper = true, Attacks_Other_Profiles;
            public int Peace_Keeper_Cool_Down = 5;
            public int Weapon_Drop_Percent;
            public int Min_Weapon_Drop_Condition_Percent = 50;
            public int Max_Weapon_Drop_Condition_Percent = 100;
            public bool Keep_Default_Loadout;
            public int Wipe_Belt_Percent = 100, Wipe_Clothing_Percent = 100, Allow_Rust_Loot_Percent = 100;
            public int Suicide_Timer = 300;
            public bool Chute;
            public int Aggro_Range = 30;
            public int DeAggro_Range = 40;
            public bool Announce_Spawn;
            public string Announcement_Text = String.Empty;
            public float Running_Speed_Boost;
            public int Spawn_Hackable_Death_Crate_Percent;
            public string Death_Crate_CustomLoot_Profile = "";
            public int Death_Crate_LockDuration = 600;
            public bool AlwaysUseLights;
            public bool Die_Instantly_From_Headshot = false;
            public List<string> Instant_Death_From_Headshot_Allowed_Weapons = new List<string>();      
        }

        public class AirDropProfile : BaseProfile
        {
            [JsonProperty(Order = 1)]
            public int Radius = 100;
        }

        public class ConfigProfile : AirDropProfile
        {
            [JsonProperty(Order = 1)]
            public bool Stationary;
            [JsonProperty(Order = 2)]
            public int Night_Time_Spawn_Amount = 0;
            [JsonProperty(Order = 3)]
            public bool UseCustomSpawns;
            [JsonProperty(Order = 4)]
            public bool ChangeCustomSpawnOnDeath;
            [JsonProperty(Order = 5)]
            public int Respawn_Timer = 60;
        }

        public class BiomeProfile : BaseProfile
        {
            [JsonIgnore]
            public bool Radius;
            [JsonIgnore]
            public bool Stationary;
            [JsonProperty(Order = 1)]
            public int Night_Time_Spawn_Amount = 0;
            [JsonIgnore]
            public bool ChangeCustomSpawnOnDeath;
            [JsonIgnore]
            public bool UseCustomSpawns;
            [JsonProperty(Order = 2)]
            public int Respawn_Timer = 60;
        }

        public class DataProfile : AirDropProfile
        {
            public DataProfile Clone()
            {
                return MemberwiseClone() as DataProfile; 
            }
            [JsonProperty(Order = 1)]
            public bool Stationary;
            [JsonProperty(Order = 2)]
            public int Night_Time_Spawn_Amount = 0;
            [JsonProperty(Order = 3)]
            public bool UseCustomSpawns;
            [JsonProperty(Order = 4)]
            public bool ChangeCustomSpawnOnDeath;
            [JsonProperty(Order = 5)]
            public int Respawn_Timer = 60;
            [JsonProperty(Order = 6)]
            public Vector3 Location;
            [JsonProperty(Order = 7)]
            public string Parent_Monument = String.Empty;
        }

        class ConfigData
        {
            public string DataPrefix = "default";
            public Global Global = new Global();
        }

        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();
            SaveConfig(configData);
        }
        protected override void LoadDefaultConfig()
        {
            LoadConfigVariables();
            Puts("Creating new config file.");
        }

        void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, True);
        }
        #endregion

        #region Messages     
        readonly Dictionary<string, string> Messages = new Dictionary<string, string>()
        {
            {"Title", "BotSpawn : " },
            {"error", "\n<color=orange>Profile commands are :</color>\nlist\nshow <duration>\nadd ProfileName\nremove ProfileName\nmove ProfileName\ntoplayer ProfileName \nspawn\nkill\n<color=orange>Spawns commands are :</color>\nedit ProfileName\naddspawn\nremovespawn\nremovespawn Number\nmovespawn Number\nloadspawns FileName" },
            {"customsaved", "Custom Location Saved @ {0}" },
            {"custommoved", "Custom Location {0} has been moved to your current position." },

            {"nonpc", "No BotSpawn npc found directly in front of you." },
            {"noNavHere", "No navmesh was found at this location.\nConsider removing this point or using Stationary : true." },
            {"editingname", "Editing spawnpoints for {0}." },
            {"addedspawn", "Spawnpoint {0} added to {1}." },
            {"removedspawn", "Removed last spawn point from {0}. {1} points remaining." },
            {"savedspawn", "Spawnpoints saved for {0}." },
            {"notediting", "You are not editing a profile. '/botspawn edit profilename'" },
            {"imported", "Spawn points imported from {0} to {1}." },
            {"nospawns", "No custom spawn points were found for profile - {0}." },
            {"targetzero", "Target amount for time of day is zero at - {0}." },
            {"notenoughspawns", "There are not enough spawn points for population at profile - {0}. Reducing population" },
            {"removednum", "Removed point {0} from {1}." },
            {"movedspawn", "Moved point {0} in {1}." },
            {"notthatmany", "Number of spawn points in {0} is less than {1}." },
            {"alreadyexists", "Custom Location already exists with the name {0}." },
            {"customremoved", "Custom Location {0} Removed." },
            {"norespawn", "Please choose a respawning profile with set location, or associated spawns file." },
            {"deployed", "'{0}' bots deployed to {1}." },
            {"ListTitle", "Custom Locations" },
            {"killed", "Non-respawning npcs of profile {0} have been destroyed." },
            {"reloaded", "Profile was reloaded from data." },
            {"noprofile", "There is no profile by that name in default or custom profiles jsons." },
            {"showduration", "Correct formate is /botspawn show <duration>" },
            {"nonpcs", "No npcs were found belonging to a profile of that name" },
            {"namenotfound", "Player '{0}' was not found" },
            {"nokits", "Kits is not installed but you have declared custom kits at {0}." },
            {"noWeapon", "A bot at {0} has no weapon. Check your kits." },
            {"numberOfBot", "There is {0} spawned bot alive." },
            {"numberOfBots", "There are {0} spawned bots alive." },
            {"dupID", "Duplicate userID save attempted. Please notify author." },
            {"noSpawn", "Failed to find spawnpoints at {0}." },
            {"noNav", "Spawn point {1} in Spawns file {0} is too far away from navmesh." }
        };
        #endregion

        #region ExternalHooks
        private string NpcGroup(NPCPlayer npc)
        {
            if (NPCPlayers.ContainsKey(npc.userID))
                return npc.GetComponent<BotData>().group;
            return "No Group";
        }

        private Dictionary<string, List<ulong>> BotSpawnBots()
        {
            var BotSpawnBots = new Dictionary<string, List<ulong>>();

            foreach (var entry in AllProfiles)
                BotSpawnBots.Add(entry.Key, new List<ulong>());

            foreach (var bot in NPCPlayers)
            {
                var bData = bot.Value.GetComponent<BotData>();
                if (BotSpawnBots.ContainsKey(bData.monumentName))
                    BotSpawnBots[bData.monumentName].Add(bot.Key);
                else
                    BotSpawnBots.Add(bData.monumentName, new List<ulong> { bot.Key });
            }
            return BotSpawnBots;
        }

        private string[] AddGroupSpawn(Vector3 location, string profileName, string group)
        {
            if (location == new Vector3() || profileName == null || group == null)
                return new string[] { "error", "Null parameter" };
            string lowerProfile = profileName.ToLower();

            foreach (var entry in AllProfiles)
            {
                if (entry.Key.ToLower() == lowerProfile)
                {
                    var profile = entry.Value;
                    if (TargetAmount(AllProfiles[entry.Key]) == 0)
                        return new string[] { "false", "Target spawn amount for time of day is zero.}" };
                    timer.Repeat(1f, TargetAmount(AllProfiles[entry.Key]), () => DeployNpcs(location, entry.Key, profile, group.ToLower(), -1));
                    return new string[] { "true", "Group successfully added" };
                }
            }
            return new string[] { "false", "Group add failed - Check profile name and try again" };
        }

        private string[] RemoveGroupSpawn(string group)
        {
            if (group == null)
                return new string[] { "error", "No group specified." };

            List<NPCPlayerApex> toDestroy = new List<NPCPlayerApex>();
            bool flag = False;
            foreach (var bot in NPCPlayers.ToDictionary(pair => pair.Key, pair => pair.Value))
            {
                if (bot.Value == null)
                    continue;
                var bData = bot.Value.GetComponent<BotData>();
                if (bData.group == group.ToLower())
                {
                    flag = True;
                    NPCPlayers[bot.Key].Kill();
                }
            }
            return flag ? new string[] { "true", $"Group {group} was destroyed." } : new string[] { "true", $"There are no bots belonging to {group}" };
        }

        private string[] CreateNewProfile(string name, string profile)
        {
            if (name == null)
                return new string[] { "error", "No name specified." };
            if (profile == null)
                return new string[] { "error", "No profile settings specified." };

            DataProfile newProfile = JsonConvert.DeserializeObject<DataProfile>(profile);

            if (storedData.DataProfiles.ContainsKey(name))
            {
                storedData.DataProfiles[name] = newProfile;
                AllProfiles[name] = newProfile;
                foreach (var npc in NPCPlayers.ToList())
                {
                    var bData = npc.Value.GetComponent<BotData>();
                    if (bData.monumentName == name)
                    {
                        bData.profile = AllProfiles[name].Clone();
                        bData.profile.Respawn_Timer = 0;
                        npc.Value.Kill();
                    }
                }
                return new string[] { "true", $"Profile {name} was updated" };
            }

            storedData.DataProfiles.Add(name, newProfile);
            SaveData();
            AllProfiles.Add(name, newProfile);
            popinfo.Add(name, new PopInfo());
            popinfo[name].spawnTracker = -1;
            return new string[] { "true", $"New Profile {name} was created." };
        }

        private string[] ProfileExists(string name)
        {
            if (name == null)
                return new string[] { "error", "No name specified." };

            if (AllProfiles.ContainsKey(name))
                return new string[] { "true", $"{name} exists." };

            return new string[] { "false", $"{name} Does not exist." };
        }

        private string[] RemoveProfile(string name)
        {
            if (name == null)
                return new string[] { "error", "No name specified." };

            if (storedData.DataProfiles.ContainsKey(name))
            {
                foreach (var bot in NPCPlayers.ToDictionary(pair => pair.Key, pair => pair.Value))
                {
                    if (bot.Value == null)
                        continue;
                    var bData = bot.Value.GetComponent<BotData>();
                    if (bData.monumentName == name)
                        NPCPlayers[bot.Key].Kill();
                }
                AllProfiles.Remove(name);
                storedData.DataProfiles.Remove(name);
                SaveData();
                return new string[] { "true", $"Profile {name} was removed." };
            }
            else
                return new string[] { "false", $"Profile {name} Does Not Exist." };
        }
        #endregion
    }
}
