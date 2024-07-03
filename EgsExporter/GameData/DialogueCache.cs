using EgsLib.ConfigFiles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EgsExporter.GameData
{
    internal partial class DialogueCache
    {
        [GeneratedRegex(@"GetReputation\((.+)\)[\s]+(>=|[>^=]|<=|[<^=])[\s]+([^\s|^""]+)", RegexOptions.Multiline)]
        private static partial Regex ReputationParser();

        private IReadOnlyList<Dialogue> Dialogues { get; }
        private IReadOnlyDictionary<string, Dialogue> NamedDialogues { get; }

        public DialogueCache(List<Dialogue> dialogues)
        {
            Dialogues = dialogues;
            NamedDialogues = Dialogues.ToDictionary(x => x.Name, x => x);
        }

        public IReadOnlyDictionary<string, string>? RequiredItems(string dialogueName)
        {
            if (!NamedDialogues.TryGetValue(dialogueName, out Dialogue? dialogue) 
                || dialogue == null || dialogue.Next == null)
                return null;

            var nexts = dialogue.Next.Where(n => n.Conditional?.Contains("CanTrade > 0") ?? false);
            if (nexts == null)
                return null;

            var response = new Dictionary<string, string>();
            foreach (var next in nexts)
            {
                // Change TD_<name>_Trade -> TD_<name>_Missions[number]
                var name = next.Dialog.Replace("Trade", "Mission");

                // Change TD_<name>_Trade -> TD_<name>_MissionComplete
                var finished = next.Dialog.Replace("Trade", "MissionComplete");

                var missions = Dialogues
                    .Where(e => e.Name.StartsWith(name) && char.IsDigit(e.Name.Last()) && e.Options != null)
                    .Select(e => new { name = e.Name, opt = e.Options!.FirstOrDefault(o => o.Next == finished) })
                    .Where(x => x.opt != null && x.opt.Conditional != null)
                    .ToDictionary(x => x.name, x => x.opt!.Conditional!.Trim(' ', '"'));

                foreach (var mission in missions)
                    response.Add(mission.Key, mission.Value);
            }

            return response;
        }

        public IReadOnlyDictionary<string, string>? RequiredReputation(string dialogueName)
        {
            if (!NamedDialogues.TryGetValue(dialogueName, out Dialogue? dialogue)
                || dialogue == null || dialogue.Next == null)
                return null;

            // Check options
            var options = dialogue.Options?
                .Where(o => o.Conditional?.Contains("GetReputation") ?? false)
                .Select(o => new { next = o.Next, match = ReputationParser().Match(o.Conditional!) })
                .Where(x => x.match.Success)
                .ToDictionary(x => x.next, x => x.match.Value) ?? [];

            // Check nexts
            var nexts = dialogue.Next?
                .Where(n => n.Conditional?.Contains("GetReputation") ?? false)
                .Select(n => new { next = n.Dialog, match = ReputationParser().Match(n.Conditional!) })
                .Where(x => x.match.Success)
                .ToDictionary(x => x.next, x => x.match.Value) ?? [];


            var response = options.Concat(nexts)
                .ToLookup(x => x.Key, x => x.Value)
                .ToDictionary(x => x.Key, g => g.First());

            return response;
        }
    }
}
