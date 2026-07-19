const SERVER = process.env.MCP_SERVER || "http://localhost:8082";

async function mcpCall(method, params = {}) {
  const res = await fetch(SERVER + "/", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ jsonrpc: "2.0", id: 1, method, params }),
  });

  const reader = res.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });
    const lines = buffer.split("\n");
    buffer = lines.pop() || "";

    for (const line of lines) {
      const trimmed = line.trim();
      if (trimmed.startsWith("data: ")) {
        try {
          const data = JSON.parse(trimmed.slice(6));
          if (data.result) {
            return data.result;
          } else if (data.error) {
            throw new Error(`MCP Error: ${JSON.stringify(data.error)}`);
          }
        } catch (e) {
          if (e.message.startsWith("MCP Error:")) throw e;
          // skip malformed SSE lines
        }
      }
    }
  }
}

async function main() {
  const [, , command, ...args] = process.argv;

  switch (command) {
    case "list": {
      const result = await mcpCall("tools/list");
      console.log("Available tools:\n");
      for (const tool of result.tools) {
        console.log(`  ${tool.name}`);
        console.log(`    ${tool.description}`);
        const props = tool.inputSchema?.properties || {};
        const required = tool.inputSchema?.required || [];
        const paramList = Object.entries(props).map(([k, v]) =>
          `${k} (${v.type})${required.includes(k) ? " *required" : ""}`
        );
        if (paramList.length) {
          console.log(`    Parameters: ${paramList.join(", ")}`);
        }
        console.log();
      }
      break;
    }

    case "create": {
      const [path, title, content] = args;
      if (!path || !title || !content) {
        console.error("Usage: node mcp-test.mjs create <path> <title> <content>");
        process.exit(1);
      }
      const result = await mcpCall("tools/call", {
        name: "docx_create",
        arguments: { path, title, content },
      });
      console.log(result.content?.[0]?.text || JSON.stringify(result));
      break;
    }

    case "read": {
      const [path] = args;
      if (!path) {
        console.error("Usage: node mcp-test.mjs read <path>");
        process.exit(1);
      }
      const result = await mcpCall("tools/call", {
        name: "docx_read",
        arguments: { path },
      });
      console.log(result.content?.[0]?.text || JSON.stringify(result));
      break;
    }

    case "info": {
      const [path] = args;
      if (!path) {
        console.error("Usage: node mcp-test.mjs info <path>");
        process.exit(1);
      }
      const result = await mcpCall("tools/call", {
        name: "docx_info",
        arguments: { path },
      });
      console.log(result.content?.[0]?.text || JSON.stringify(result));
      break;
    }

    default:
      console.log(`
MCP Office Module Test Tool — Server: ${SERVER}

Commands:
  node mcp-test.mjs list                          List all available tools
  node mcp-test.mjs create <path> <title> <text>  Create a .docx
  node mcp-test.mjs read <path>                   Read text from a .docx
  node mcp-test.mjs info <path>                   Get metadata about a .docx

Examples:
  node mcp-test.mjs list
  node mcp-test.mjs create test.docx "My Title" "Hello from MCP!"
  node mcp-test.mjs read test.docx
  node mcp-test.mjs info test.docx
`);
  }
}

main().catch((err) => {
  console.error(err.message);
  process.exit(1);
});