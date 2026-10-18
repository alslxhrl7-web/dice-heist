using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiceHeist.Server
{
    /// <summary>
    /// 꾸미기 가챠. 확률과 보상은 gacha.json에서 읽는다(사용자가 나중에 정한다). 사이버머니로만 뽑고, 현금은 오가지 않는다.
    /// 확률은 weight / 전체 합이고, 화면에 그대로 보인다(확률형 아이템 확률 공개).
    /// </summary>
    public sealed class GachaConfig
    {
        [JsonPropertyName("placeholder")] public bool Placeholder { get; set; }
        [JsonPropertyName("cost")] public long Cost { get; set; } = 10_000;
        [JsonPropertyName("tenPullCost")] public long TenPullCost { get; set; } = 90_000;
        [JsonPropertyName("rarities")] public List<GachaRarity> Rarities { get; set; } = new();
        [JsonPropertyName("items")] public List<GachaItem> Items { get; set; } = new();

        public static GachaConfig Load(string path)
        {
            var cfg = JsonSerializer.Deserialize<GachaConfig>(File.ReadAllText(path)) ?? new GachaConfig();
            cfg.Validate();
            return cfg;
        }

        public void Validate()
        {
            if (Items.Count == 0) throw new InvalidOperationException("gacha.json에 아이템이 없습니다.");
            if (Items.Any(i => i.Weight <= 0)) throw new InvalidOperationException("gacha.json의 weight는 0보다 커야 합니다.");
            if (Items.Select(i => i.Id).Distinct().Count() != Items.Count) throw new InvalidOperationException("gacha.json에 같은 id가 두 번 있습니다.");
            if (Cost <= 0 || TenPullCost <= 0) throw new InvalidOperationException("gacha.json의 가격은 0보다 커야 합니다.");
        }

        public double TotalWeight => Items.Sum(i => i.Weight);
        public GachaItem Find(string id) => Items.FirstOrDefault(i => i.Id == id);

        /// <summary>한 번 뽑는다. 서버 난수(RandomNumberGenerator)로 weight 비율대로.</summary>
        public GachaItem Draw(Func<double> random = null)
        {
            double r = (random ?? (() => RandomNumberGenerator.GetInt32(int.MaxValue) / (double)int.MaxValue))() * TotalWeight;
            foreach (var item in Items)
            {
                r -= item.Weight;
                if (r < 0) return item;
            }
            return Items[^1];
        }

        /// <summary>화면용: 아이템마다 확률(%)까지.</summary>
        public object Public() => new
        {
            placeholder = Placeholder,
            cost = Cost,
            tenPullCost = TenPullCost,
            rarities = Rarities,
            items = Items.Select(i => new { i.Id, i.Name, i.Icon, i.Rarity, chance = i.Weight / TotalWeight }).ToArray(),
        };
    }

    public sealed class GachaRarity
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("color")] public string Color { get; set; }
    }

    public sealed class GachaItem
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("icon")] public string Icon { get; set; }
        [JsonPropertyName("rarity")] public string Rarity { get; set; }
        [JsonPropertyName("weight")] public double Weight { get; set; }
    }
}
