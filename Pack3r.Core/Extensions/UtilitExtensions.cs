using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Pack3r.Extensions;

public static class UtilitExtensions
{
    public static T Get<T>(this ServiceProvider serviceProvider) where T : class
    {
        return serviceProvider.GetRequiredService<T>();
    }
}
