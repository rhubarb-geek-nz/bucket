# rhubarb-geek-nz/bucket

Simple mechanism to transfer files between legacy systems.

It originated when trying to work with old machines which couldn't use modern HTTPS.

| Method | Url | Content | Comment |
| ------ | --- | ------- | ------- |
| GET | / | Listing as a table in HTML | Listing in browser |
| GET | /filename | Content of file with disposition header | Should download as file |
| POST | / | file upload form | Use with curl --form mechanism |
| PUT | /filename | Content of file | Write file directly |

Only a single flat level directory is supported.

It is intended for private use, not on the public internet.

The Kestel listener is configured using [appsettings.json](appsettings.json)

It uses Aot, minimal ASP.NET and Docker to create a modern deployment.
