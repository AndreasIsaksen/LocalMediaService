FROM caddy:2.11.4-alpine

RUN addgroup -S gateway \
    && adduser -S -D -H -G gateway gateway \
    && setcap -r /usr/bin/caddy \
    && chown -R gateway:gateway /data /config

COPY Caddyfile /etc/caddy/Caddyfile

USER gateway
