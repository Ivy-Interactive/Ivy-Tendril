---
icon: Globe
searchHints:
  - ngrok
  - tunnel
  - expose
  - webhook
  - public url
  - remote access
---

# Ngrok

<Ingress>
Use ngrok to expose your local Tendril instance to the internet, enabling webhooks and remote access during development.
</Ingress>

## Overview

Tendril runs locally by default, which means external services (like jam.dev webhooks or GitHub webhook events) cannot reach it. Ngrok creates a secure tunnel from a public URL to your local Tendril instance.

## Prerequisites

1. Install ngrok from [ngrok.com](https://ngrok.com)
2. Sign up for a free ngrok account and authenticate:

```bash
ngrok config add-authtoken YOUR_AUTH_TOKEN
```

## Starting a Tunnel

With Tendril running on its default port, start an ngrok tunnel:

```bash
ngrok http 5010
```

Ngrok outputs a public forwarding URL:

```
Forwarding  https://abc123.ngrok-free.app -> http://localhost:5010
```

Use this URL anywhere an external service needs to reach Tendril.

## Using with Webhooks

Replace `localhost:5010` with your ngrok URL when configuring webhook integrations. For example, a jam.dev webhook would point to:

```
https://abc123.ngrok-free.app/api/inbox
```

<Callout type="warning">
The ngrok URL changes each time you restart the tunnel unless you use a reserved domain on a paid plan. Update your webhook configurations accordingly.
</Callout>

## Using with GitHub Webhooks

To receive GitHub webhook events locally:

1. Start the ngrok tunnel
2. In your GitHub repository, go to **Settings > Webhooks > Add webhook**
3. Set the Payload URL to your ngrok URL (e.g. `https://abc123.ngrok-free.app/api/inbox`)
4. Set Content type to `application/json`
5. Choose which events to send

## Security Considerations

<Callout type="tip">
Always set an API key in your `config.yaml` under `api.apiKey` when exposing Tendril via ngrok. Without it, anyone with your tunnel URL can access the API.
</Callout>

- Ngrok tunnels are publicly accessible — treat them as production endpoints from a security perspective
- Use `ngrok http 5010 --basic-auth "user:password"` to add an extra layer of authentication at the tunnel level
- Shut down the tunnel when not actively needed
- Review the ngrok web inspector at `http://localhost:4040` to monitor incoming requests

## Inspecting Traffic

Ngrok provides a local web inspector at `http://localhost:4040` where you can:

- View all requests passing through the tunnel
- Inspect request and response headers and bodies
- Replay requests for debugging
