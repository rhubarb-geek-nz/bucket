/**************************************************************************
 *
 *  Copyright 2024, Roger Brown
 *
 *  This file is part of rhubarb-geek-nz/bucket.
 *
 *  This program is free software: you can redistribute it and/or modify it
 *  under the terms of the GNU General Public License as published by the
 *  Free Software Foundation, either version 3 of the License, or (at your
 *  option) any later version.
 * 
 *  This program is distributed in the hope that it will be useful, but WITHOUT
 *  ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or
 *  FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License for
 *  more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with this program.  If not, see <http://www.gnu.org/licenses/>
 *
 */

using Microsoft.Net.Http.Headers;
using System.Net;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace Bucket
{
	internal class BucketService
    {
        private readonly ILogger<BucketService> logger;
        private readonly IWebHostEnvironment env;
        private readonly IConfiguration config;
        private readonly string ? wwwAuthenticate;
        private readonly List<string> ? authorization;
        private readonly char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
        private readonly char[] problemHrefChars = ['?', '*', ':'];
        private readonly char[] pathSeparators = [':','/','\\'];
        private readonly Dictionary<Regex,string> contentTypes= new Dictionary<Regex, String>();

        public BucketService(IConfiguration config, IWebHostEnvironment env,ILogger<BucketService> logger)
        {
            this.env = env;
            this.logger = logger;
            this.config = config;

            logger.LogInformation("Web root path {WebRootPath}", new object[] { env.WebRootPath });

            var signtool = config.GetSection("Bucket");

            authorization = signtool.GetSection(HeaderNames.Authorization).Get<List<string>>();
            wwwAuthenticate = signtool.GetValue<string>(HeaderNames.WWWAuthenticate);

            var contentTypeSection = signtool.GetSection(HeaderNames.ContentType);

            if (contentTypeSection != null)
            {
                foreach (var contentType in contentTypeSection.GetChildren())
                {
                    string key = contentType.Key;
                    string ? value = contentType.Value;
                    if (value != null)
                    {
                        contentTypes.Add(new Regex(key), value);
                    }
                }
            }
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var request = context.Request;

            if (authorization != null)
            {
                string ? auth = request.Headers.Authorization;

                if ((auth==null) || !authorization.Contains(auth))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;

                    if (wwwAuthenticate!=null)
                    {
                        context.Response.Headers.WWWAuthenticate = wwwAuthenticate;
                    }

                    return;
                }
            }

            logger.LogInformation("{Method} {Path}", new object[] { request.Method, request.Path });

            string path = request.Path.HasValue ? request.Path.Value : "/";

            if (Path.DirectorySeparatorChar != '/')
            {
                path = path.Replace('/', Path.DirectorySeparatorChar);
            }

            if (path[0] != Path.DirectorySeparatorChar)
            {
                context.Response.StatusCode= (int)HttpStatusCode.BadRequest;
                return;
            }

            if (path.Length > 1)
            {
                int i = path.Length;

                while (i-- > 1)
                {
                    char c = path[i];

                    if (invalidFileNameChars.Contains(c))
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                        return;
                    }
                }
            }

            switch (request.Method)
            {
                case "GET":
                    try
                    {
                        var response = context.Response;
                        if (path.Length == 1 && path[0] == Path.DirectorySeparatorChar)
                        {
                            response.ContentType = MediaTypeNames.Text.Html;

                            var writer = response.BodyWriter;
                            string head = "<!DOCTYPE HTML PUBLIC \"-//IETF//DTD HTML//EN\"><HTML><HEAD><TITLE>bucket</TITLE></HEAD><BODY><TABLE><TR><TH>name</TH><TH>length</TH><TH>date</TH></TR>";
                            await writer.WriteAsync(Encoding.ASCII.GetBytes(head));

                            List<FileInfo> files = new List<FileInfo>();

                            foreach (var file in Directory.EnumerateFiles(env.WebRootPath, "*", SearchOption.TopDirectoryOnly))
                            {
                                files.Add(new FileInfo(file));
                            }

                            files.Sort((x, y) => x.Name.CompareTo(y.Name));

                            foreach (var file in files)
                            {
                                string name = HttpUtility.HtmlEncode(file.Name);
                                string href= HttpUtility.UrlPathEncode(file.Name);
                                foreach (char c in problemHrefChars)
                                {
                                    if (href.Contains(c))
                                    {
                                        StringBuilder sb = new StringBuilder();

                                        foreach (char x in file.Name)
                                        {
                                            sb.Append($"%{(int)x:X2}");
                                        }

                                        href = sb.ToString();

                                        break;
                                    }
                                }
                                string row = $"<TR><TD><A HREF=\"/{href}\">{name}</A></TD><TD>{file.Length}</TD><TD>{file.CreationTime:o}</TD></TR>";
                                await writer.WriteAsync(Encoding.ASCII.GetBytes(row));
                            }

                            string tail = "</TABLE></BODY></HTML>";
                            await writer.WriteAsync(Encoding.ASCII.GetBytes(tail));
                        }
                        else
                        {
                            string filename = env.WebRootPath + path;
                            long length = new FileInfo(filename).Length;
                            string? contentType = null;
                            string baseName = path;
                            int index = path.LastIndexOfAny(pathSeparators);
                            if (index != -1)
                            {
                                baseName = path.Substring(index + 1);
                            }

                            foreach (var ct in contentTypes)
                            {
                                if (ct.Key.Match(baseName).Success)
                                {
                                    contentType = ct.Value;

                                    break;
                                }
                            }

                            if (contentType == null)
                            {
                                contentType = MediaTypeNames.Application.Octet;
                                ContentDisposition contentDisposition = new ContentDisposition("attachment");

                                contentDisposition.FileName = Path.GetFileName(filename);

                                response.Headers.ContentDisposition = contentDisposition.ToString();
                            }

                            response.ContentType = contentType;
                            response.ContentLength = length;

                            using (var outfile = File.Open(filename, FileMode.Open))
                            {
                                await outfile.CopyToAsync(response.Body);
                            }
                        }
                    }
                    catch (FileNotFoundException ex)
                    {
                        logger.LogInformation(ex, "FileNotFoundException {FileName}", ex.FileName);

                        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    }
                    break;
                case "POST":
                    if (request.HasFormContentType)
                    {
                        IFormCollection form = await request.ReadFormAsync();
                        IFormFileCollection formFiles = form.Files;

                        foreach (IFormFile formFile in formFiles)
                        {
                            string fileName = formFile.FileName;
                            int index = fileName.LastIndexOfAny(pathSeparators);

                            if (index != -1)
                            {
                                fileName = fileName.Substring(index + 1);
                            }

                            if (fileName.Length < 1)
                            {
                                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                                return;
                            }

                            foreach (char c in fileName)
                            {
                                if (invalidFileNameChars.Contains(c))
                                {
                                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                                    return;
                                }
                            }

                            logger.LogInformation("{Method} {FileName}", new object[] { request.Method, fileName});

                            string filePath = env.WebRootPath + Path.DirectorySeparatorChar + fileName;

                            bool exists = File.Exists(filePath);

                            using (var file = File.Open(filePath, FileMode.Create))
                            {
                                await formFile.CopyToAsync(file);
                            }

                            if (formFile.Length != new FileInfo(filePath).Length)
                            {
                                context.Response.StatusCode = (int)HttpStatusCode.InsufficientStorage;

                                File.Delete(filePath);
                            }
                            else
                            {
                                context.Response.StatusCode = (int)(exists ? HttpStatusCode.NoContent : HttpStatusCode.Created);
                            }
                        }
                    }
                    else
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    }
                    break;
                case "PUT":
                    {
                        long? length = request.ContentLength;

                        if (length.HasValue)
                        {
                            string filePath = env.WebRootPath + path;
                            bool exists = File.Exists(filePath);

                            using (var file = File.Open(filePath, FileMode.Create))
                            {
                                await request.Body.CopyToAsync(file);
                            }

                            if (length.Value != new FileInfo(filePath).Length)
                            {
                                context.Response.StatusCode = (int)HttpStatusCode.InsufficientStorage;

                                File.Delete(filePath);
                            }
                            else
                            {
                                context.Response.StatusCode = (int)(exists ? HttpStatusCode.NoContent : HttpStatusCode.Created);
                            }
                        }
                        else
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.LengthRequired;
                        }
                    }
                    break;
                case "DELETE":
                    try
                    {
                        string filename = env.WebRootPath + path;
                        long length = new FileInfo(filename).Length;

                        File.Delete(filename);

                        context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                    }
                    catch (FileNotFoundException ex)
                    {
                        logger.LogInformation(ex, "FileNotFoundException {FileName}", ex.FileName);

                        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    }
                    break;
                default:
                    context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    break;
            }
        }
    }
}
