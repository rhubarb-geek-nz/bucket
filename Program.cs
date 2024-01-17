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

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

var config = app.Services.GetRequiredService<IConfiguration>();
var webenv = app.Services.GetRequiredService<IWebHostEnvironment>();
var logger = app.Services.GetRequiredService<ILogger<Bucket.BucketService>>();

app.Run(new Bucket.BucketService(config, webenv, logger).InvokeAsync);

app.Run();
