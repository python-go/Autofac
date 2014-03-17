using System;
using System.ServiceModel;
using System.ServiceModel.Description;
using Autofac.Core;
using Autofac.Extras.DynamicProxy2;
using Autofac.Integration.Wcf;
using Castle.DynamicProxy;
using NUnit.Framework;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

namespace Autofac.Extras.Tests.DynamicProxy2
{
    [TestFixture]
    public class InterceptTransparentProxyFixture
    {
        private static readonly Uri TestServiceAddress = new Uri("http://localhost:80/Temporary_Listen_Addresses/ITestService");

        [Test(Description = "The service being intercepted must be registered as an interface.")]
        public void ServiceMustBeInterface()
        {
            var builder = new ContainerBuilder();
            builder.Register(c => new object()).InterceptTransparentProxy();
            var container = builder.Build();

            Assert.Throws<DependencyResolutionException>(() => container.Resolve<object>());
        }

        [Test(Description = "The instance being intercepted must be a transparent proxy.")]
        public void ServiceMustBeTransparentProxy()
        {
            var builder = new ContainerBuilder();
            builder.Register(c => new object()).As<ITestService>().InterceptTransparentProxy();
            var container = builder.Build();

            var exception = Assert.Throws<DependencyResolutionException>(() => container.Resolve<ITestService>());

            Assert.That(exception.Message, Is.StringContaining(typeof(object).FullName));
        }

        [Test(Description = "The instance must implement the additional interfaces provided.")]
        public void ProxyMustImplementAdditionalInterfaces()
        {
            var builder = new ContainerBuilder();
            builder.Register(c => CreateChannelFactory()).SingleInstance();
            builder.Register(c => c.Resolve<ChannelFactory<ITestService>>().CreateChannel())
                .SingleInstance()
                .InterceptTransparentProxy(typeof(ICloneable), typeof(IFormattable));
            
            var container = builder.Build();

            var exception = Assert.Throws<DependencyResolutionException>(() => container.Resolve<ITestService>());

            Assert.That(exception.Message, Is.StringContaining(typeof(ICloneable).FullName));
            Assert.That(exception.Message, Is.StringContaining(typeof(IFormattable).FullName));
        }

        [Test(Description = "Issue 361: WCF service client code should allow interception to occur.")]
        public void ServiceClientInterceptionIsPossible()
        {
            // Build the service-side container
            var sb = new ContainerBuilder();
            sb.RegisterType<TestService>().As<ITestService>();

            // Build the client-side container with interception
            // around the client proxy. Issue 361 was that there
            // seemed to be trouble around getting this to work.
            var cb = new ContainerBuilder();
            cb.RegisterType<TestServiceInterceptor>();
            cb.Register(c => CreateChannelFactory()).SingleInstance();
            cb
                .Register(c => c.Resolve<ChannelFactory<ITestService>>().CreateChannel())
                .InterceptTransparentProxy(typeof(IClientChannel))
                .InterceptedBy(typeof(TestServiceInterceptor))
                .UseWcfSafeRelease();

            using (var sc = sb.Build())
            {
                // Start the self-hosted test service
                var host = CreateTestServiceHost(sc);
                host.Open();
                try
                {
                    using (var cc = cb.Build())
                    {
                        // Make a call through the client to the service -
                        // it should be intercepted.
                        var client = cc.Resolve<ITestService>();
                        Assert.AreEqual("interceptor", client.DoWork(), "The call through the client proxy to the service was not intercepted.");
                    }
                }
                finally
                {
                    host.Close();
                }
            }
        }

		[Test]
		public void ServiceClientInterceptionIsPossibleWithMixins()
		{
			var options = new ProxyGenerationOptions();
			options.AddMixinInstance(new Dictionary<int, int>());

			// Build the service-side container
			var sb = new ContainerBuilder();
			sb.RegisterType<TestService>().As<ITestService>();

			// Build the client-side container with interception
			// around the client proxy. Issue 361 was that there
			// seemed to be trouble around getting this to work.
			var cb = new ContainerBuilder();
			cb.Register(c => CreateChannelFactory()).SingleInstance();
			cb
				.Register(c => c.Resolve<ChannelFactory<ITestService>>().CreateChannel())
				.InterceptTransparentProxy(options, typeof(IClientChannel))
				.UseWcfSafeRelease();

			using (var sc = sb.Build())
			{
				// Start the self-hosted test service
				var host = CreateTestServiceHost(sc);
				host.Open();
				try
				{
					using (var cc = cb.Build())
					{
						// Make a call through the client to the service -
						// it should be intercepted.
						var client = cc.Resolve<ITestService>();
						var dict = client as IDictionary<int, int>;

						Assert.IsNotNull(dict);

						dict.Add(1, 2);

						Assert.AreEqual(2, dict[1]);

						dict.Clear();

						Assert.IsEmpty(dict);
					}
				}
				finally
				{
					host.Close();
				}
			}
		}

		[Test]
		public void ServiceClientInterceptionIsPossibleForSpecificMethod()
		{
			var options = new ProxyGenerationOptions(new InterceptOnlyOtherWork());

			// Build the service-side container
			var sb = new ContainerBuilder();
			sb.RegisterType<TestService>().As<ITestService>();

			// Build the client-side container with interception
			// around the client proxy. Issue 361 was that there
			// seemed to be trouble around getting this to work.
			var cb = new ContainerBuilder();
			cb.RegisterType<PrependInterceptor>();
			cb.RegisterType<AppendInterceptor>();
			cb.Register(c => CreateChannelFactory()).SingleInstance();
			cb
				.Register(c => c.Resolve<ChannelFactory<ITestService>>().CreateChannel())
				.InterceptTransparentProxy(options, typeof(IClientChannel))
				.InterceptedBy(typeof(PrependInterceptor), typeof(AppendInterceptor))
				.UseWcfSafeRelease();

			using (var sc = sb.Build())
			{
				// Start the self-hosted test service
				var host = CreateTestServiceHost(sc);
				host.Open();
				try
				{
					using (var cc = cb.Build())
					{
						// Make a call through the client to the service -
						// it should be intercepted.
						var client = cc.Resolve<ITestService>();

						Assert.AreEqual("service", client.DoWork());
						Assert.AreEqual("pre-work-post", client.DoOtherWork());
					}
				}
				finally
				{
					host.Close();
				}
			}
		}

		[Test]
		public void ServiceClientInterceptionIsPossibleWithSpecificInterceptors()
		{
			var options = new ProxyGenerationOptions { Selector = new MyInterceptorSelector() };

			// Build the service-side container
			var sb = new ContainerBuilder();
			sb.RegisterType<TestService>().As<ITestService>();

			// Build the client-side container with interception
			// around the client proxy. Issue 361 was that there
			// seemed to be trouble around getting this to work.
			var cb = new ContainerBuilder();
			cb.RegisterType<PrependInterceptor>();
			cb.RegisterType<AppendInterceptor>();
			cb.Register(c => CreateChannelFactory()).SingleInstance();
			cb
				.Register(c => c.Resolve<ChannelFactory<ITestService>>().CreateChannel())
				.InterceptTransparentProxy(options, typeof(IClientChannel))
				.InterceptedBy(typeof(PrependInterceptor), typeof(AppendInterceptor))
				.UseWcfSafeRelease();

			using (var sc = sb.Build())
			{
				// Start the self-hosted test service
				var host = CreateTestServiceHost(sc);
				host.Open();
				try
				{
					using (var cc = cb.Build())
					{
						// Make a call through the client to the service -
						// it should be intercepted.
						var client = cc.Resolve<ITestService>();

						Assert.AreEqual("pre-service", client.DoWork());
						Assert.AreEqual("work-post", client.DoOtherWork());
					}
				}
				finally
				{
					host.Close();
				}
			}
		}

        private static ServiceHost CreateTestServiceHost(ILifetimeScope container)
        {
            var host = new ServiceHost(typeof(TestService), TestServiceAddress);
            host.AddServiceEndpoint(typeof(ITestService), new BasicHttpBinding(), "");
            host.AddDependencyInjectionBehavior<ITestService>(container);
            host.Description.Behaviors.Add(new ServiceMetadataBehavior { HttpGetEnabled = true, HttpGetUrl = TestServiceAddress });
            return host;
        }

        static ChannelFactory<ITestService> CreateChannelFactory()
        {
            return new ChannelFactory<ITestService>(new BasicHttpBinding(), new EndpointAddress(TestServiceAddress));
        }

        [ServiceContract]
        public interface ITestService
        {
            [OperationContract]
            string DoWork();

			[OperationContract]
			string DoOtherWork();
        }

        public class TestService : ITestService
        {
            public string DoWork()
            {
                return "service";
            }

			public string DoOtherWork()
			{
				return "work";
			}
        }

        public class TestServiceInterceptor : IInterceptor
        {
            public void Intercept(IInvocation invocation)
            {
                if (invocation.Method.Name == "DoWork")
                {
                    invocation.ReturnValue = "interceptor";
                }
                else
                {
                    invocation.Proceed();
                }
            }
        }

		public class PrependInterceptor : IInterceptor
		{
			public void Intercept(IInvocation invocation)
			{
				invocation.Proceed();
				invocation.ReturnValue = "pre-" + invocation.ReturnValue;
			}
		}

		public class AppendInterceptor : IInterceptor
		{
			public void Intercept(IInvocation invocation)
			{
				invocation.Proceed();
				if (invocation.Method.Name == "DoOtherWork")
				{
					invocation.ReturnValue += "-post";
				}
			}
		}

		public class InterceptOnlyOtherWork : IProxyGenerationHook
		{

			public void MethodsInspected()
			{
			}

			public void NonProxyableMemberNotification(Type type, MemberInfo memberInfo)
			{
			}

			public bool ShouldInterceptMethod(Type type, MethodInfo methodInfo)
			{
				return methodInfo.Name.Equals("DoOtherWork");
			}

		}

		class MyInterceptorSelector : IInterceptorSelector
		{
			public IInterceptor[] SelectInterceptors(Type type, MethodInfo method, IInterceptor[] interceptors)
			{
				return method.Name == "DoWork"
					? interceptors.OfType<PrependInterceptor>().ToArray<IInterceptor>()
					: interceptors.OfType<AppendInterceptor>().ToArray<IInterceptor>();
			}
		}
    }
}
