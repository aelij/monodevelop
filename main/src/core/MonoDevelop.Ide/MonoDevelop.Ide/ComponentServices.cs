using System;
using System.Collections.Generic;
using System.Composition.Hosting;
using System.Linq;
using System.Reflection;
using MonoDevelop.Core.Annotations;

namespace MonoDevelop.Ide
{
    public abstract class ComponentServices
    {
        public abstract T GetService<T>();
    }

    public class MefComponentServices : ComponentServices
    {
        private readonly CompositionHost host;

        public MefComponentServices(params Assembly[] assemblies)
            : this(assemblies.AsEnumerable())
        {
        }

        public MefComponentServices(IEnumerable<Assembly> assemblies)
            : this(CreateCompositionContext(assemblies))
        {
        }

        public MefComponentServices(CompositionHost host)
        {
            this.host = host;
        }

        private static CompositionHost CreateCompositionContext([NotNull] IEnumerable<Assembly> assemblies)
        {
            var config = new ContainerConfiguration()
                .WithAssembly(typeof(CoreExtensions).Assembly)  // MonoDevelop.Core
                .WithAssembly(typeof(MefComponentServices).Assembly) // MonoDevelop.Ide
                .WithAssemblies(assemblies);
            return config.CreateContainer();
        }

        public override T GetService<T>()
        {
            return host.GetExport<T>();
        }
    }
}