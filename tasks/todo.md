# In-app MCP server for the AirPlay mirror

- [x] Hand-rolled Streamable HTTP handler (TcpListener on 127.0.0.1:47615, JSON replies): no ASP.NET dependency, nothing loaded at startup
- [x] ScriptCommand.Png shared by the pipe capture and the MCP capture
- [x] McpServer.cs: initialize, tools/list, tools/call mirror_status and mirror_capture
- [x] App.xaml.cs: start with the app, dispose on exit, port taken is logged to mcp.log
- [x] McpServerTests.cs: initialize, tools/list, mirror status while not mirrored
- [x] Bump patch version, Release build, tests, MSI
