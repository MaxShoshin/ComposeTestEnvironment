namespace ComposeTestEnvironment.xUnit
{
    internal record DockerPort (ushort ExposedPort, ushort PublicPort, string Protocol);
}
