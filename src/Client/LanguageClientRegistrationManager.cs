﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Shared;
using OmniSharp.Extensions.LanguageServer.Shared;

namespace OmniSharp.Extensions.LanguageServer.Client
{
    internal class LanguageClientRegistrationManager : IRegisterCapabilityHandler, IUnregisterCapabilityHandler, IRegistrationManager, IDisposable
    {
        private readonly ISerializer _serializer;
        private readonly ILogger<LanguageClientRegistrationManager> _logger;
        private readonly ConcurrentDictionary<string, Registration> _registrations;
        private readonly ReplaySubject<IEnumerable<Registration>> _registrationSubject;

        public LanguageClientRegistrationManager(ISerializer serializer, ILogger<LanguageClientRegistrationManager> logger)
        {
            _serializer = serializer;
            _logger = logger;
            _registrations = new ConcurrentDictionary<string, Registration>(StringComparer.OrdinalIgnoreCase);
            _registrationSubject = new ReplaySubject<IEnumerable<Registration>>(1);
        }

        Task<Unit> IRequestHandler<RegistrationParams, Unit>.Handle(RegistrationParams request, CancellationToken cancellationToken)
        {
            lock (this)
            {
                Register(request.Registrations.ToArray());
            }

            _registrationSubject.OnNext(_registrations.Values);
            return Unit.Task;
        }

        Task<Unit> IRequestHandler<UnregistrationParams, Unit>.Handle(UnregistrationParams request, CancellationToken cancellationToken)
        {
            lock (this)
            {
                foreach (var item in request.Unregisterations ?? new UnregistrationContainer())
                {
                    _registrations.TryRemove(item.Id, out _);
                }
            }

            _registrationSubject.OnNext(_registrations.Values);
            return Unit.Task;
        }

        public void RegisterCapabilities(ServerCapabilities serverCapabilities)
        {
            foreach (var registrationOptions in LspHandlerDescriptorHelpers.GetStaticRegistrationOptions(
                serverCapabilities
            ))
            {
                var method = LspHandlerTypeDescriptorHelper.GetMethodForRegistrationOptions(registrationOptions);
                if (method == null)
                {
                    _logger.LogWarning("Unable to find method for given {@RegistrationOptions}", registrationOptions);
                    continue;
                }

                var reg = new Registration {
                    Id = registrationOptions.Id,
                    Method = method,
                    RegisterOptions = registrationOptions
                };
                _registrations.AddOrUpdate(registrationOptions.Id, x => reg, (a, b) => reg);
            }

            if (serverCapabilities.Workspace == null)
            {
                _registrationSubject.OnNext(_registrations.Values);
                return;
            }

            foreach (var registrationOptions in LspHandlerDescriptorHelpers.GetStaticRegistrationOptions(
                serverCapabilities
                   .Workspace
            ))
            {
                var method = LspHandlerTypeDescriptorHelper.GetMethodForRegistrationOptions(registrationOptions);
                if (method == null)
                {
                    // TODO: Log this
                    continue;
                }

                var reg = new Registration {
                    Id = registrationOptions.Id,
                    Method = method,
                    RegisterOptions = registrationOptions
                };
                _registrations.AddOrUpdate(registrationOptions.Id, x => reg, (a, b) => reg);
            }
        }

        private void Register(params Registration[] registrations)
        {
            foreach (var registration in registrations)
            {
                Register(registration);
            }
        }

        private void Register(Registration registration)
        {
            var registrationType = LspHandlerTypeDescriptorHelper.GetRegistrationType(registration.Method);
            if (registrationType == null)
            {
                _registrations.AddOrUpdate(registration.Id, x => registration, (a, b) => registration);
                return;
            }

            var deserializedRegistration = new Registration {
                Id = registration.Id,
                Method = registration.Method,
                RegisterOptions = registration.RegisterOptions is JToken token
                    ? token.ToObject(registrationType, _serializer.JsonSerializer)
                    : registration.RegisterOptions
            };
            _registrations.AddOrUpdate(deserializedRegistration.Id, x => deserializedRegistration, (a, b) => deserializedRegistration);
        }

        public IObservable<IEnumerable<Registration>> Registrations => _registrationSubject.AsObservable();
        public IEnumerable<Registration> CurrentRegistrations => _registrations.Values;

        public IEnumerable<Registration> GetRegistrationsForMethod(string method) => _registrations.Select(z => z.Value).Where(x => x.Method == method);

        public IEnumerable<Registration> GetRegistrationsMatchingSelector(DocumentSelector documentSelector) =>
            _registrations
               .Select(z => z.Value)
               .Where(
                    x => x.RegisterOptions is ITextDocumentRegistrationOptions ro && ro.DocumentSelector
                                                                                       .Join(
                                                                                            documentSelector,
                                                                                            z => z.HasLanguage ? z.Language :
                                                                                                z.HasScheme ? z.Scheme :
                                                                                                z.HasPattern ? z.Pattern : string.Empty,
                                                                                            z => z.HasLanguage ? z.Language :
                                                                                                z.HasScheme ? z.Scheme :
                                                                                                z.HasPattern ? z.Pattern : string.Empty, (a, b) => a
                                                                                        )
                                                                                       .Any(x => x.HasLanguage || x.HasPattern || x.HasScheme)
                );

        public void Dispose() => _registrationSubject.Dispose();
    }
}
