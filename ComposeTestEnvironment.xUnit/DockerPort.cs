namespace ComposeTestEnvironment.xUnit
{
    internal record DockerPort (in ushort ExposedPort, ushort PublicPort);
}