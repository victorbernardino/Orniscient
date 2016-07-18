﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Derivco.Orniscient.Proxy.Grains.Models;
using Orleans;

namespace Derivco.Orniscient.Proxy.Grains
{
    public interface ITypeMethodsGrain : IGrainWithStringKey
    {
        Task<List<GrainMethod>> GetAvailableMethods();
        Task InvokeGrainMethod(string id, string methodName, string parametersJson);
    }
}
