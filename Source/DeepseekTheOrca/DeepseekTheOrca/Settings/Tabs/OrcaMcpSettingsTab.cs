using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaMcpSettingsTab : OrcaSettingsTab
    {
        private Vector2 scrollPosition;
        private string httpMcpMaxResultCharsBuffer;

        public override string Id
        {
            get { return "mcp"; }
        }

        public override string Label
        {
            get { return "DTO_SettingsPageMcp".Translate().ToString(); }
        }

        public override int Order
        {
            get { return 70; }
        }

        public override void OnSelected(OrcaSettingsContext context)
        {
            scrollPosition = Vector2.zero;
        }

        public override void Draw(Rect rect, OrcaSettingsContext context)
        {
            DeepseekTheOrcaSettings settings = context == null ? null : context.settings;
            if (settings == null)
            {
                return;
            }

            EnsureServers(settings);
            float viewHeight = Mathf.Max(rect.height, 260f + settings.httpMcpServers.Count * 230f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, viewHeight);
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            listing.CheckboxLabeled("DTO_EnableHttpMcp".Translate(), ref settings.enableHttpMcp, "DTO_EnableHttpMcpTooltip".Translate());
            listing.TextFieldNumericLabeled("DTO_HttpMcpMaxResultChars".Translate(), ref settings.httpMcpMaxResultChars, ref httpMcpMaxResultCharsBuffer, 500, 20000);
            listing.Gap();

            if (listing.ButtonText("DTO_HttpMcpAddServer".Translate()))
            {
                settings.httpMcpServers.Add(new OrcaHttpMcpServerSettings
                {
                    name = "MCP " + (settings.httpMcpServers.Count + 1),
                    enabled = true
                });
            }

            if (settings.httpMcpServers.Count == 0)
            {
                listing.Label("DTO_HttpMcpNoServers".Translate());
            }

            for (int i = 0; i < settings.httpMcpServers.Count; i++)
            {
                OrcaHttpMcpServerSettings server = settings.httpMcpServers[i];
                if (server == null)
                {
                    continue;
                }

                listing.GapLine();
                Rect headerRect = listing.GetRect(32f);
                Widgets.Label(new Rect(headerRect.x, headerRect.y + 6f, headerRect.width - 130f, 24f), "DTO_HttpMcpServer".Translate() + " " + (i + 1));
                Rect removeRect = new Rect(headerRect.xMax - 120f, headerRect.y, 120f, 30f);
                if (Widgets.ButtonText(removeRect, "DTO_HttpMcpRemoveServer".Translate()))
                {
                    settings.httpMcpServers.RemoveAt(i);
                    i--;
                    continue;
                }

                listing.CheckboxLabeled("DTO_HttpMcpServerEnabled".Translate(), ref server.enabled);
                listing.Label("DTO_HttpMcpServerName".Translate());
                server.name = listing.TextEntry(server.name ?? "");
                listing.Label("DTO_HttpMcpUrl".Translate(), -1f, "DTO_HttpMcpUrlTooltip".Translate());
                server.url = listing.TextEntry(server.url ?? "");
                listing.Label("DTO_HttpMcpBearerToken".Translate(), -1f, "DTO_HttpMcpBearerTokenTooltip".Translate());
                server.bearerToken = listing.TextEntry(server.bearerToken ?? "");
            }

            listing.Gap();
            listing.Label("DTO_HttpMcpNote".Translate());

            listing.End();
            Widgets.EndScrollView();
        }

        private static void EnsureServers(DeepseekTheOrcaSettings settings)
        {
            if (settings.httpMcpServers == null)
            {
                settings.httpMcpServers = new List<OrcaHttpMcpServerSettings>();
            }
        }
    }
}
