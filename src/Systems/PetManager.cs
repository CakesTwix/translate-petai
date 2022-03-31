using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace PetAI
{
    public class PetManager : ModSystem
    {

        public ConcurrentDictionary<long, PetData> petMap;

        private ICoreServerAPI sapi;

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            sapi = api;
            api.Event.SaveGameLoaded += OnLoad;
            api.Event.GameWorldSave += OnSave;
        }

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return forSide == EnumAppSide.Server;
        }

        public void UpdatePet(Entity pet, bool? hasDied = null)
        {
            var tameable = pet.GetBehavior<EntityBehaviorTameable>();
            if (tameable == null || String.IsNullOrEmpty(tameable.ownerId)) { return; }
            if (hasDied == null) { hasDied = !pet.Alive; }

            var petData = petMap.GetOrAdd(pet.EntityId, new PetData());
            petData.alive = !(bool)hasDied;
            petData.lastSeenAt = pet.ServerPos.AsBlockPos;
            petData.ownerId = tameable.ownerId;
            petData.petId = pet.EntityId;
            petData.petClass = sapi.World.ClassRegistry.GetEntityClassName(pet.GetType());
            petData.petName = pet.GetBehavior<EntityBehaviorNameTag>()?.DisplayName;
            petData.petType = String.Format("{0}:item-creature-{1}", pet.Code.Domain, pet.Code.Path);

            if ((bool)hasDied)
            {
                petData.deadUntil = sapi.World.Calendar.TotalHours + PetConfig.Current.petRespawnCooldown;
                using (MemoryStream ms = new MemoryStream())
                {
                    using (BinaryWriter writer = new BinaryWriter(ms, Encoding.UTF8))
                    {
                        pet.ToBytes(writer, false);
                        writer.Flush();
                        petData.deadPetBytes = ms.ToArray();
                    }
                }
            }
            else
            {
                petData.deadPetBytes = null;
                petData.deadUntil = 0;
            }
        }

        public long RevivePet(long petId, BlockPos pos)
        {
            var data = petMap[petId];
            using (MemoryStream ms = new MemoryStream(data.deadPetBytes))
            {
                using (BinaryReader reader = new BinaryReader(ms, Encoding.UTF8))
                {
                    Entity pet = sapi.World.ClassRegistry.CreateEntity(data.petClass);
                    pet.FromBytes(reader, false);
                    sapi.World.SpawnEntity(pet);
                    pet.ServerPos.SetPos(pos.ToVec3d());
                    return pet.EntityId;
                }
            }
        }

        public List<PetDataSmall> GetPetsForPlayer(string playerUID)
        {
            List<PetDataSmall> list = new List<PetDataSmall>();
            foreach (var pet in petMap.Values)
            {
                if (pet.ownerId == playerUID)
                {
                    var data = new PetDataSmall();
                    data.ownerId = pet.ownerId;
                    data.petId = pet.petId;
                    data.petName = pet.petName;
                    data.petType = pet.petType;
                    list.Add(data);
                }
            }
            return list;
        }

        private void OnSave()
        {
            sapi.WorldManager.SaveGame.StoreData("petmanager", SerializerUtil.Serialize(petMap));
        }

        private void OnLoad()
        {
            byte[] data = sapi.WorldManager.SaveGame.GetData("petmanager");
            petMap = data == null ? new ConcurrentDictionary<long, PetData>() : SerializerUtil.Deserialize<ConcurrentDictionary<long, PetData>>(data);
        }
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class PetData
    {
        public string ownerId;
        public long petId;
        public string petType;
        public string petName;

        public string petClass;
        public bool alive;
        public BlockPos lastSeenAt;
        public BlockPos nestLocation;
        public double deadUntil;
        public byte[] deadPetBytes;
    }
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class PetDataSmall
    {
        public string ownerId;
        public long petId;
        public string petType;
        public string petName;
    }
}