using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.IO;

namespace Hooks
{
	// Used for human-friendly entity id=>string conversion.
	public static class EntityDB
	{
		static Dictionary<int, string> Names;
		static Dictionary<int, int> Owners;
		static int[] HeroEntities;

		public static string GetStatus(int entityId)
		{
			if (entityId == 1)
				return "Game Entity";
			if (entityId == 2)
				return "Player 1";
			if (entityId == 3)
				return "Player 2";
			if (!Names.ContainsKey(entityId) || !Owners.ContainsKey(entityId))
			{
				return null;
			}
			var ownerName = "";
			if (Names.ContainsKey(HeroEntities[Owners[entityId]]))
			{
				ownerName = Names[HeroEntities[Owners[entityId]]];
			}
			return String.Format("{0} ({1}#{2})", Names[entityId], ownerName, 1 + Owners[entityId]);
		}

		public static void SetName(int entityId, string cardID)
		{
			var edef = DefLoader.Get().GetEntityDef(cardID);
			if (edef != null)
			{
				Names[entityId] = edef.GetName();
			}
			else
			{
				DefLoader.Get().LoadFullDef(cardID, (cardId, def, data) =>
					{
						Names[entityId] = def.GetEntityDef().GetName();
					});
			}
		}

		public static void SetOwner(int entityId, int controllerId)
		{
			Owners[entityId] = controllerId - 1;
		}

		public static void SetHero(int controllerId, int heroId)
		{
			HeroEntities[controllerId - 1] = heroId;
		}

		public static void Reset()
		{
			Names = new Dictionary<int, string>();
			Owners = new Dictionary<int, int>();
			HeroEntities = new int[2];
		}
	}

	public static class TagFormat
	{
		public static string GetValue(Network.Entity.Tag tag)
		{
			return GetValue(tag.Name, tag.Value);
		}

		public static string GetValue(int name, int value)
		{
			string result = null;
			if (TypedTags.ContainsKey((GAME_TAG)name))
			{
				result = Enum.GetName(TypedTags[(GAME_TAG)name], value);
			}
			return result;
		}

		static Dictionary<GAME_TAG, Type> TypedTags = new Dictionary<GAME_TAG, Type>
		{
			{ GAME_TAG.STATE, typeof(TAG_STATE) },
			{ GAME_TAG.ZONE, typeof(TAG_ZONE) },
			{ GAME_TAG.STEP, typeof(TAG_STEP) },
			{ GAME_TAG.NEXT_STEP, typeof(TAG_STEP) },
			{ GAME_TAG.PLAYSTATE, typeof(TAG_PLAYSTATE) },
			{ GAME_TAG.CARDTYPE, typeof(TAG_CARDTYPE) },
			{ GAME_TAG.MULLIGAN_STATE, typeof(TAG_MULLIGAN) },
			{ GAME_TAG.CARD_SET, typeof(TAG_CARD_SET) },
			{ GAME_TAG.CLASS, typeof(TAG_CLASS) },
			{ GAME_TAG.RARITY, typeof(TAG_RARITY) },
			{ GAME_TAG.FACTION, typeof(TAG_FACTION) },
			{ GAME_TAG.CARDRACE, typeof(TAG_RACE) },
			{ GAME_TAG.ENCHANTMENT_BIRTH_VISUAL, typeof(TAG_ENCHANTMENT_VISUAL) },
			{ GAME_TAG.ENCHANTMENT_IDLE_VISUAL, typeof(TAG_ENCHANTMENT_VISUAL) },
			{ GAME_TAG.GOLD_REWARD_STATE, typeof(TAG_GOLD_REWARD_STATE) }
		};
	}

	public class TagListSerializer : JsonConverter
	{
		public override bool CanConvert(Type objectType)
		{
			return typeof(List<Network.Entity.Tag>).IsAssignableFrom(objectType);
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			var tags = value as List<Network.Entity.Tag>;
			writer.WriteStartObject();
			foreach (var tag in tags)
			{
				var name = Enum.GetName(typeof(GAME_TAG), tag.Name);
				var val = tag.Value;
				var stringVal = TagFormat.GetValue(tag);
				writer.WritePropertyName(name);
				if (stringVal == null)
				{
					writer.WriteValue(val);
				}
				else
				{
					writer.WriteValue(stringVal);
				}
			}
			writer.WriteEndObject();
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			throw new NotSupportedException();
		}
	}

	public class TagChangeSerializer : JsonConverter
	{
		public override bool CanConvert(Type objectType)
		{
			return typeof(Network.HistTagChange).IsAssignableFrom(objectType);
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			throw new NotSupportedException();
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			var hist = value as Network.HistTagChange;
			writer.WriteStartObject();
			writer.WritePropertyName("Entity");
			writer.WriteValue(hist.Entity);
			writer.WritePropertyName("__status");
			writer.WriteValue(EntityDB.GetStatus(hist.Entity));
			writer.WritePropertyName("Change");
			writer.WriteStartObject();
			writer.WritePropertyName(Enum.GetName(typeof(GAME_TAG), hist.Tag));
			var stringVal = TagFormat.GetValue(hist.Tag, hist.Value);
			if (stringVal == null)
			{
				writer.WriteValue(hist.Value);
			}
			else
			{
				writer.WriteValue(stringVal);
			}
			writer.WriteEndObject();
			writer.WritePropertyName("Type");
			writer.WriteValue(Enum.GetName(typeof(Network.PowerType), hist.Type));
			writer.WriteEndObject();
		}
	}

	public class EntitySerializer : JsonConverter
	{
		public override bool CanConvert(Type objectType)
		{
			return typeof(Network.Entity).IsAssignableFrom(objectType);
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			throw new NotSupportedException();
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			var entity = value as Network.Entity;
			if (!string.IsNullOrEmpty(entity.CardID))
			{
				EntityDB.SetName(entity.ID, entity.CardID);
			}
			var ownerId = 0;
			foreach (var tag in entity.Tags)
			{
				if (tag.Name == (int)GAME_TAG.CONTROLLER)
				{
					ownerId = tag.Value;
				}
			}
			if (ownerId != 0)
			{
				EntityDB.SetOwner(entity.ID, ownerId);
			}
			writer.WriteStartObject();
			writer.WritePropertyName("CardID");
			writer.WriteValue(entity.CardID);
			writer.WritePropertyName("ID");
			writer.WriteValue(entity.ID);
			writer.WritePropertyName("__status");
			writer.WriteValue(EntityDB.GetStatus(entity.ID));
			writer.WritePropertyName("Tags");
			serializer.Serialize(writer, entity.Tags);
			writer.WriteEndObject();
		}
	}

	public class CreateGameSerializer : JsonConverter
	{
		public override bool CanConvert(Type objectType)
		{
			return typeof(Network.HistCreateGame).IsAssignableFrom(objectType);
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			throw new NotSupportedException();
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			var create = value as Network.HistCreateGame;
			EntityDB.Reset();
			foreach (var playerData in create.Players)
			{
				var player = playerData.Player;
				var heroId = 0;
				var controllerId = 0;
				foreach (var tag in player.Tags)
				{
					if (tag.Name == (int)GAME_TAG.HERO_ENTITY)
					{
						heroId = tag.Value;
					}
					if (tag.Name == (int)GAME_TAG.CONTROLLER)
					{
						controllerId = tag.Value;
					}
				}
				if (heroId != 0 && controllerId != 0)
				{
					EntityDB.SetHero(controllerId, heroId);
				}
			}
			writer.WriteStartObject();
			writer.WritePropertyName("Game");
			serializer.Serialize(writer, create.Game);
			writer.WritePropertyName("Players");
			serializer.Serialize(writer, create.Players);
			writer.WritePropertyName("Type");
			writer.WriteValue(Enum.GetName(typeof(Network.PowerType), create.Type));
			writer.WriteEndObject();
		}
	}

	public class PowTypeSerializer : JsonConverter
	{
		public override bool CanConvert(Type objectType)
		{
			return objectType == typeof(Network.PowerType);
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			throw new NotSupportedException();
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			writer.WriteValue(Enum.GetName(typeof(Network.PowerType), value));
		}
	}

	[RuntimeHook]
	public class JsonHistLogger
	{
		public TextWriter log;
		JsonSerializer js;

		object OnCall(string typeName, string methodName, object thisArg, object[] args)
		{
			if (log == null)
			{
				var fs = File.Open("entity.log", FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
				log = new StreamWriter(fs);
				log.Write('[');
			}
			if (typeName == "GameState" && methodName == "OnPowerHistory")
			{
				var historyList = args[0] as List<Network.PowerHistory>;
				js.Serialize(log, historyList);
				log.WriteLine();
				log.Write(',');
				log.Flush();
			}
			return null;
		}

		public JsonHistLogger()
		{
			js = JsonSerializer.Create();
			js.Converters.Add(new EntitySerializer());
			js.Converters.Add(new TagListSerializer());
			js.Converters.Add(new TagChangeSerializer());
			js.Converters.Add(new CreateGameSerializer());
			js.Converters.Add(new PowTypeSerializer());

			HookRegistry.Register(OnCall);
		}
	}
}