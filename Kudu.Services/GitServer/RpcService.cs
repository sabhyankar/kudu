#region License

// Copyright 2010 Jeremy Skinner (http://www.jeremyskinner.co.uk)
//  
// Licensed under the Apache License, Version 2.0 (the "License"); 
// you may not use this file except in compliance with the License. 
// You may obtain a copy of the License at 
// 
// http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software 
// distributed under the License is distributed on an "AS IS" BASIS, 
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. 
// See the License for the specific language governing permissions and 
// limitations under the License.
// 
// The latest version of this file can be found at http://github.com/JeremySkinner/git-dot-aspx

// This file was modified from the one found in git-dot-aspx

#endregion

namespace Kudu.Services.GitServer
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Compression;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.ServiceModel;
    using System.ServiceModel.Web;
    using System.Threading;
    using Kudu.Core.Deployment;
    using Kudu.Core.SourceControl.Git;

    // Handles {project}/git-upload-pack and {project}/git-receive-pack
    [ServiceContract]
    public class RpcService
    {
        private readonly IDeploymentManager _deploymentManager;
        private readonly IGitServer _gitServer;

        public RpcService(IGitServer gitServer, IDeploymentManager deploymentManager)
        {
            _gitServer = gitServer;
            _deploymentManager = deploymentManager;
        }

        [Description("Handles a 'git pull' command.")]
        [WebInvoke(UriTemplate = "git-upload-pack")]
        public HttpResponseMessage UploadPack(HttpRequestMessage request)
        {
            var memoryStream = new MemoryStream();
            _gitServer.Upload(GetInputStream(request), memoryStream);
            memoryStream.Flush();
            memoryStream.Position = 0;

            return CreateResponse(memoryStream, "application/x-git-{0}-result".With("upload-pack"));
        }

        [Description("Handles a 'git push' command.")]
        [WebInvoke(UriTemplate = "git-receive-pack")]
        public HttpResponseMessage ReceivePack(HttpRequestMessage request)
        {
            var memoryStream = new MemoryStream();
            _gitServer.Receive(GetInputStream(request), memoryStream);
            memoryStream.Flush();
            memoryStream.Position = 0;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    _deploymentManager.Deploy();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error deploying");
                    Debug.WriteLine(ex.Message);
                }
            });

            return CreateResponse(memoryStream, "application/x-git-{0}-result".With("receive-pack"));
        }

        private Stream GetInputStream(HttpRequestMessage request)
        {
            if (request.Content.Headers.ContentEncoding.Contains("gzip"))
            {
                return new GZipStream(request.Content.ContentReadStream, CompressionMode.Decompress);
            }

            return request.Content.ContentReadStream;
        }

        private static HttpResponseMessage CreateResponse(MemoryStream stream, string mediaType)
        {
            var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            // REVIEW: Why is it that we do not write an empty Content-Type here, like for InfoRefsController?

            var response = new HttpResponseMessage();
            response.Content = content;
            response.WriteNoCache();
            return response;
        }
    }
}