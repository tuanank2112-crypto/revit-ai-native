using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutodeskNativeAgent.Core.Contracts;
using Xunit;

namespace AutodeskNativeAgent.Core.Tests
{
    /// <summary>
    /// Verifies the wire contract between the C# add-in (ProtocolCatalog) and the
    /// TypeScript MCP server (apps/agent-mcp/src/index.ts). If these drift apart,
    /// the MCP proxy will call pipe methods the add-in never implements.
    /// </summary>
    public class EnvelopeContractTests
    {
        private static string ResolveMcpSourcePath()
        {
            // Test assembly lives under <repo>/tests/unit/AutodeskNativeAgent.Core.Tests/bin/<cfg>/net8.0/.
            // Walk up until we find the repo marker (AutodeskNativeAgent.sln).
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AutodeskNativeAgent.sln")))
            {
                dir = dir.Parent;
            }

            if (dir == null)
            {
                return null;
            }

            return Path.Combine(dir.FullName, "apps", "agent-mcp", "src", "index.ts");
        }

        // MCP tool name -> pipe method it must invoke, from index.ts switch.
        private static readonly Dictionary<string, string> ExpectedMappings = new()
        {
            ["revit_get_status"] = "status",
            ["revit_get_capabilities"] = "capabilities",
            ["revit_inspect_document"] = "document.get_info",
            ["revit_inspect_selection"] = "selection.get",
            ["revit_query_elements"] = "element.query",
            ["revit_validate_plan"] = "plan.validate",
            ["revit_preview_plan"] = "plan.preview",
            ["revit_commit_plan"] = "plan.commit",
            ["revit_confirm_plan"] = "plan.confirm",
            ["revit_get_job_status"] = "job.status",
            ["revit_cancel_job"] = "job.cancel",
            ["revit_rollback_job"] = "job.rollback",
            ["revit_get_audit_log"] = "audit.log",
        };

        [Fact]
        public void Mcp_tool_to_method_mapping_matches_index_ts()
        {
            string mcpSourcePath = ResolveMcpSourcePath();
            Assert.True(
                mcpSourcePath != null && File.Exists(mcpSourcePath),
                "MCP source (index.ts) not found; test must run within the repo.");

            string source = File.ReadAllText(mcpSourcePath);

            foreach (var pair in ExpectedMappings)
            {
                // tool name must appear in the tool definition list
                Assert.True(
                    source.Contains($"name: '{pair.Key}'", StringComparison.Ordinal),
                    $"MCP tool '{pair.Key}' missing from index.ts tools list.");

                // the switch must route the tool to the expected pipe method
                Assert.True(
                    source.Contains($"method = '{pair.Value}'", StringComparison.Ordinal),
                    $"MCP tool '{pair.Key}' does not map to pipe method '{pair.Value}' in index.ts.");
            }
        }

        [Fact]
        public void Every_router_method_has_a_supported_catalog_entry()
        {
            // The add-in router is C# (AgentRequestRouter) and uses ProtocolCatalog.
            // Every method constant must be listed as supported in ProtocolCatalog.All.
            var catalog = ProtocolCatalog.All;
            foreach (var expected in ExpectedMappings.Values.Distinct())
            {
                Assert.NotNull(ProtocolCatalog.Find(expected));
                Assert.True(ProtocolCatalog.IsSupported(expected), $"ProtocolCatalog marks '{expected}' unsupported.");
            }
            Assert.NotEmpty(catalog);
        }
    }
}
