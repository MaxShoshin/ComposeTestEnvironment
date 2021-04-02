using System.Collections.Generic;

namespace ComposeTestEnvironment.xUnit
{
    public interface IPortRenter
    {
        IReadOnlyList<ushort> Rent(string serviceName, int portCount);
    }
}
