Steps — VPS-Level Actions (you need to perform)
Validate Cloudflare is in Full (Strict) SSL mode — since Caddy uses tls internal, Cloudflare must be set to "Full (Strict)" with an origin certificate, or change the Caddyfile to use real ACME certs (tls your@email.com) and set Cloudflare to "Full (Strict)". Currently there may be a gap where Cloudflare→origin traffic uses a self-signed cert.

Enable Cloudflare WAF rules — activate Cloudflare's managed ruleset (available on free tier) to catch common attack patterns (SQLi, XSS, path traversal) before they reach your API.

Restrict VPS firewall to Cloudflare IPs only — ports 80/443 on the VPS should only accept connections from Cloudflare's IP ranges. This prevents attackers from bypassing Cloudflare by hitting the VPS IP directly. Use ufw or iptables to whitelist only Cloudflare IPs.

Enable Cloudflare rate limiting — as a supplementary layer to the application-level rate limiter, configure Cloudflare's rate limiting rules (free tier includes basic rules) for the /chat endpoints.

Audit Docker Compose volumes — in compose.prod.yml:29-30, Ollama data is mounted to both /var/lib/ollama and /root/.ollama on the same volume. Verify this doesn't cause conflicts. Also ensure the host volume mount points have restrictive permissions.

Add SSH hardening on VPS — disable root SSH login, use key-based auth only, and consider fail2ban. This is orthogonal to the API but critical for the overall security posture.