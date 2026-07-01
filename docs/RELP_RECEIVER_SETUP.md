# RELP receiver setup

DeltaZulu.Agent forwards filtered resource records through `DeltaZulu.DurableBuffer` and a TCP tunnel. The agent is a RELP client/forwarder; it is not a syslog daemon or production receiver. Use a dedicated receiver such as rsyslog or syslog-ng at the network edge and route accepted payloads into the downstream collector pipeline.

These examples are operational starting points for lab validation. Validate plain RELP first, then enable TLS with a certificate policy that matches `config/dzagent.yaml`.

## Ports and payload model

| Mode | Typical port | Agent setting | Receiver expectation |
| --- | --- | --- | --- |
| Plain RELP | `2514` or lab-only `6514` | `relp.useTls: false` | RELP over TCP; protect with host firewall or private network. |
| RELP/TLS | `6514` | `relp.useTls: true` | Receiver presents a certificate trusted by system trust or the configured thumbprint allow-list. |

The forwarded RELP message body is a MessagePack `DeliveryBatch` envelope, not a normalized syslog event. Keep the receiver rule simple: accept RELP, write the raw binary message unchanged, and let downstream systems decode the delivery envelope with the shared DeltaZulu MessagePack codec.

## rsyslog plain RELP lab receiver

Install the RELP input module package for your distribution, then create a receiver snippet such as `/etc/rsyslog.d/30-deltazulu-relp.conf`:

```conf
module(load="imrelp")

template(name="DeltaZuluRawMessage" type="string" string="%msg%\n")

ruleset(name="deltazulu_forwarder") {
  action(type="omfile" file="/var/log/deltazulu/forwarder.ndjson" template="DeltaZuluRawMessage")
}

input(type="imrelp" port="2514" ruleset="deltazulu_forwarder")
```

Create the output directory, restart rsyslog, and point the agent at the receiver:

```bash
sudo install -d -o syslog -g adm -m 0750 /var/log/deltazulu
sudo rsyslogd -N1
sudo systemctl restart rsyslog
```

Use this only on trusted networks or localhost. Prefer TLS for cross-host traffic.

## rsyslog RELP/TLS receiver

The exact TLS package names and option availability vary by distribution. This snippet shows the intended policy shape:

```conf
module(load="imrelp")
module(load="gtls")

global(
  DefaultNetstreamDriver="gtls"
  DefaultNetstreamDriverCAFile="/etc/rsyslog.d/tls/ca.pem"
  DefaultNetstreamDriverCertFile="/etc/rsyslog.d/tls/server.pem"
  DefaultNetstreamDriverKeyFile="/etc/rsyslog.d/tls/server.key"
)

template(name="DeltaZuluRawMessage" type="string" string="%msg%\n")

ruleset(name="deltazulu_forwarder_tls") {
  action(type="omfile" file="/var/log/deltazulu/forwarder.ndjson" template="DeltaZuluRawMessage")
}

input(
  type="imrelp"
  port="6514"
  tls="on"
  tls.authmode="name"
  tls.permittedpeer=["agent-01.example.com"]
  ruleset="deltazulu_forwarder_tls"
)
```

When the agent uses `certificateValidation: SystemTrust`, the receiver certificate chain must validate against the agent host trust store. When the agent uses `certificateValidation: Thumbprint`, add the receiver certificate thumbprint to `allowedServerCertificateThumbprints` in the forwarder YAML.

## syslog-ng plain RELP lab receiver

Install syslog-ng with RELP support, then add a snippet such as `/etc/syslog-ng/conf.d/deltazulu-relp.conf`:

```conf
source s_deltazulu_relp {
  syslog(transport("relp") port(2514));
};

destination d_deltazulu_forwarder {
  file("/var/log/deltazulu/forwarder.ndjson" template("${MESSAGE}\n"));
};

log {
  source(s_deltazulu_relp);
  destination(d_deltazulu_forwarder);
};
```

Validate and reload:

```bash
sudo install -d -o syslog -g adm -m 0750 /var/log/deltazulu
sudo syslog-ng --syntax-only
sudo systemctl restart syslog-ng
```

## syslog-ng RELP/TLS receiver

Use this as a policy template and adapt paths, peer identity, and certificate settings to your syslog-ng version:

```conf
source s_deltazulu_relp_tls {
  syslog(
    transport("relp")
    port(6514)
    tls(
      key-file("/etc/syslog-ng/tls/server.key")
      cert-file("/etc/syslog-ng/tls/server.pem")
      ca-dir("/etc/syslog-ng/tls/ca.d")
      peer-verify(required-trusted)
    )
  );
};

destination d_deltazulu_forwarder_tls {
  file("/var/log/deltazulu/forwarder.ndjson" template("${MESSAGE}\n"));
};

log {
  source(s_deltazulu_relp_tls);
  destination(d_deltazulu_forwarder_tls);
};
```

## Agent configuration checklist

1. Set `relp.endpoints` to the receiver host and port.
2. Set `relp.useTls` to match the receiver listener.
3. For TLS, choose one certificate policy:
   - `SystemTrust` for certificates chaining to the agent host trust store.
   - `Thumbprint` for lab or pinned deployments where the receiver certificate thumbprint is explicitly allowed.
   - `Disabled` only for isolated diagnostics; do not use it for production traffic.
4. Keep a persistent `buffer.path` on durable local storage.
5. Run the daemon collector smoke test before using a production receiver, then run the receiver with a small host-neutral syslog fixture and confirm the output file receives MessagePack delivery batches.

## Validation notes

- Receiver snippets should preserve the RELP message body without parsing or rewriting it.
- Restrict listener firewall rules to known agent hosts.
- Monitor agent diagnostics for accepted, sent, acknowledged, retried, dead-lettered, rejected, and oldest-buffered-age counters.
- Keep daemon collector mode limited to local validation; production receivers should be separately operated and monitored.
