# Docker Setup

In order to run TShock in a docker container, you would need to have mountpoints for
 - `/tshock` (TShock config files, logs and crash reports)
 - `/worlds`
 - `/plugins`
 - `/server` (optional, if you want to mount TShock's cwd)

These folders can be mounted using `-v <host_folder>:<container>`

Open ports can also be passed through using `-p <host_port>:<container_port>`.
 - `7777` for Terraria
 - `7878` for TShock's REST API

For Example:
```bash
docker run -p 7777:7777 -p 7878:7878 \
           -v /home/cider/tshock/:/tshock \
           -v /home/cider/.local/share/Terraria/Worlds:/worlds \
           -v /home/cider/tshock/plugins:/plugins \
           --rm -it ghcr.io/pryaxis/tshock:latest \
           -world /worlds/backflip.wld -motd "OMFG DOCKER"
```

## Building custom images

Occasionally, it may be necessary to adjust TShock with customizations that are not included in the upstream project.
Therefore, these changes are also not available in the officially provided Docker images.

To build and load a Docker image from your local checkout, use the following `buildx` command:

```bash
docker buildx build -t tshock:latest --load .
```

It is also possible to build [multi-platform images](https://docs.docker.com/build/building/multi-platform/) for TShock (e.g. an image targeting `arm64`, on a host that is not `arm64`):

```bash
docker buildx build -t tshock:linux-arm64 --platform linux/arm64 --load .
```
