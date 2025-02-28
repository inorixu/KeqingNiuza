﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Http
{
    internal sealed partial class RedirectHandler : StandardHttpMessageHandler
    {
        private readonly StandardHttpMessageHandler _initialInnerHandler;       // Used for initial request
        private readonly StandardHttpMessageHandler _redirectInnerHandler;      // Used for redirects; this allows disabling auth
        private readonly int _maxAutomaticRedirections;

        public RedirectHandler(int maxAutomaticRedirections, StandardHttpMessageHandler initialInnerHandler, StandardHttpMessageHandler redirectInnerHandler)
        {
            Debug.Assert(initialInnerHandler != null);
            Debug.Assert(redirectInnerHandler != null);
            Debug.Assert(maxAutomaticRedirections > 0);

            _maxAutomaticRedirections = maxAutomaticRedirections;
            _initialInnerHandler = initialInnerHandler;
            _redirectInnerHandler = redirectInnerHandler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (NetEventSource.IsEnabled) NetEventSource.Enter(this, request, cancellationToken);

            HttpResponseMessage response = await _initialInnerHandler.SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            uint redirectCount = 0;
            Uri redirectUri;
            while ((redirectUri = GetUriForRedirect(request.RequestUri, response)) != null)
            {
                redirectCount++;

                if (redirectCount > _maxAutomaticRedirections)
                {
                    // If we exceed the maximum number of redirects
                    // then just return the 3xx response.
                    if (NetEventSource.IsEnabled)
                    {
                        NetEventSource.Error(this, $"Exceeded max number of redirects. Redirect from {request.RequestUri} to {redirectUri} blocked.");
                    }

                    break;
                }

                response.Dispose();

                // Clear the authorization header.
                request.Headers.Authorization = null;

                // Set up for the redirect
                request.RequestUri = redirectUri;
                if (RequestRequiresForceGet(response.StatusCode, request.Method))
                {
                    request.Method = HttpMethod.Get;
                    request.Content = null;
                    request.Headers.TransferEncodingChunked = false;
                }

                // Issue the redirected request.
                response = await _redirectInnerHandler.SendRequestAsync(request, cancellationToken).ConfigureAwait(false);
            }

            if (NetEventSource.IsEnabled) NetEventSource.Exit(this);

            return response;
        }

        private Uri GetUriForRedirect(Uri requestUri, HttpResponseMessage response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.Moved:
                case HttpStatusCode.Found:
                case HttpStatusCode.SeeOther:
                case HttpStatusCode.TemporaryRedirect:
                case HttpStatusCode.MultipleChoices:
                case (HttpStatusCode)308: // HttpStatusCode.PermanentRedirect
                    break;

                default:
                    return null;
            }

            Uri location = response.Headers.Location;
            if (location == null)
            {
                return null;
            }

            // Ensure the redirect location is an absolute URI.
            if (!location.IsAbsoluteUri)
            {
                location = new Uri(requestUri, location);
            }

            // Per https://tools.ietf.org/html/rfc7231#section-7.1.2, a redirect location without a
            // fragment should inherit the fragment from the original URI.
            string requestFragment = requestUri.Fragment;
            if (!string.IsNullOrEmpty(requestFragment))
            {
                string redirectFragment = location.Fragment;
                if (string.IsNullOrEmpty(redirectFragment))
                {
#if NETSTANDARD2_0
                    // .NET Framework 4.7.2 / 4.8 UriBuilder will always append the fragment marker ('#') to fragment starting with '#',
                    // while .NET Core will only append the fragment marker if not already present.
                    if (requestFragment.StartsWith("#"))
                    {
                        requestFragment = requestFragment.Substring(1);
                    }
#endif
                    location = new UriBuilder(location) { Fragment = requestFragment }.Uri;
                }
            }

            // Disallow automatic redirection from secure to non-secure schemes
            if (HttpUtilities.IsSupportedSecureScheme(requestUri.Scheme) && !HttpUtilities.IsSupportedSecureScheme(location.Scheme))
            {
                if (NetEventSource.IsEnabled)
                {
                    NetEventSource.Error(this, $"Insecure https to http redirect from '{requestUri}' to '{location}' blocked.");
                }

                return null;
            }

            return location;
        }

        private static bool RequestRequiresForceGet(HttpStatusCode statusCode, HttpMethod requestMethod)
        {
            switch (statusCode)
            {
                case HttpStatusCode.Moved:
                case HttpStatusCode.Found:
                case HttpStatusCode.SeeOther:
                case HttpStatusCode.MultipleChoices:
                    return requestMethod == HttpMethod.Post;
                default:
                    return false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _initialInnerHandler.Dispose();
                _redirectInnerHandler.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

