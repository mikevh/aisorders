// Demo UI. Talks only to API Management — never to the Function App directly,
// and never to Service Bus. Everything it does is something a caller could do
// with curl; the page just makes the async gap visible while it happens.
//
// window.DEMO_CONFIG is written by scripts/deploy-web.ps1 into config.js.

(function () {
  'use strict';

  var cfg = window.DEMO_CONFIG || {};
  var gateway = (cfg.gatewayUrl || '').replace(/\/+$/, '');
  var subscriptionKey = cfg.subscriptionKey || '';

  var els = {
    form: document.getElementById('order-form'),
    submitBtn: document.getElementById('submit-btn'),
    customerId: document.getElementById('customerId'),
    customerName: document.getElementById('customerName'),
    sku: document.getElementById('sku'),
    quantity: document.getElementById('quantity'),
    unitPrice: document.getElementById('unitPrice'),
    simulateFailure: document.getElementById('simulateFailure'),
    totalPreview: document.getElementById('total-preview'),
    presetSmall: document.getElementById('preset-small'),
    presetLarge: document.getElementById('preset-large'),
    replayBtn: document.getElementById('replay-btn'),
    replayResult: document.getElementById('replay-result'),
    body: document.getElementById('orders-body'),
    pollingState: document.getElementById('polling-state'),
    portalLink: document.getElementById('portal-link'),
    configNote: document.getElementById('config-note')
  };

  // Session-scoped only. These orders live in Table Storage; this list is just
  // what this browser tab has submitted.
  var orders = [];
  var pollTimer = null;

  // ---------------------------------------------------------------- helpers

  function money(n) {
    return Number(n || 0).toFixed(2);
  }

  function currentTotal() {
    return Number(els.quantity.value || 0) * Number(els.unitPrice.value || 0);
  }

  function refreshTotal() {
    els.totalPreview.textContent = 'Total ' + money(currentTotal());
  }

  function statusClass(status) {
    if (status === 'Completed') return 'good';
    if (status === 'Retrying' || status === 'Rejected') return 'warn';
    return 'neutral';
  }

  function headers() {
    return {
      'Content-Type': 'application/json',
      'Ocp-Apim-Subscription-Key': subscriptionKey
    };
  }

  // Reads problem+json when the gateway or the function returns one, so the
  // page shows the actual reason rather than a bare status code.
  function describeFailure(response, payload) {
    if (payload && payload.detail) return payload.detail;
    if (payload && payload.title) return payload.title;
    if (response.status === 401) return 'Rejected at the gateway: no valid subscription key.';
    if (response.status === 429) return 'Rate limited by the gateway.';
    return 'HTTP ' + response.status;
  }

  function readBody(response) {
    return response.text().then(function (text) {
      if (!text) return null;
      try { return JSON.parse(text); } catch (e) { return { detail: text }; }
    });
  }

  // ---------------------------------------------------------------- render

  function render() {
    if (orders.length === 0) {
      els.body.innerHTML = '<tr class="empty"><td colspan="6">Nothing submitted yet.</td></tr>';
      return;
    }

    els.body.innerHTML = orders.map(function (o) {
      var detail = o.error
        ? o.error
        : (o.failureReason || (o.attemptCount > 1 ? 'redelivered' : ''));

      return '<tr>' +
        '<td class="mono">' + (o.orderId ? o.orderId.slice(0, 8) : '—') + '</td>' +
        '<td>' + escapeHtml(o.customerName || '') + '</td>' +
        '<td class="num">' + money(o.orderTotal) + '</td>' +
        '<td><span class="pill ' + statusClass(o.status) + '">' + escapeHtml(o.status || 'Unknown') + '</span></td>' +
        '<td class="num">' + (o.attemptCount == null ? '—' : o.attemptCount) + '</td>' +
        '<td class="mono">' + escapeHtml(detail) + '</td>' +
        '</tr>';
    }).join('');
  }

  function escapeHtml(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  // ---------------------------------------------------------------- polling

  // Only orders that have not reached a terminal state are polled, so the
  // loop stops on its own once everything settles.
  function pending() {
    return orders.filter(function (o) {
      return o.orderId && o.status !== 'Completed' && o.status !== 'Rejected' && !o.error;
    });
  }

  function poll() {
    var open = pending();

    if (open.length === 0) {
      els.pollingState.textContent = 'idle';
      clearInterval(pollTimer);
      pollTimer = null;
      return;
    }

    els.pollingState.textContent = 'polling ' + open.length + ' order' + (open.length === 1 ? '' : 's');

    open.forEach(function (o) {
      fetch(gateway + '/orders/' + encodeURIComponent(o.orderId), { headers: headers() })
        .then(function (r) {
          return readBody(r).then(function (payload) {
            if (!r.ok) {
              // A 404 right after submission is normal: the row may not be
              // readable yet. Anything else is worth surfacing.
              if (r.status !== 404) o.error = describeFailure(r, payload);
              return;
            }
            o.status = payload.status;
            o.attemptCount = payload.attemptCount;
            o.failureReason = payload.failureReason;
            o.orderTotal = payload.orderTotal;
          });
        })
        .catch(function (e) { o.error = e.message; })
        .then(render);
    });
  }

  function startPolling() {
    if (pollTimer) return;
    pollTimer = setInterval(poll, 2000);
    poll();
  }

  // ---------------------------------------------------------------- actions

  function submitOrder(e) {
    e.preventDefault();
    els.submitBtn.disabled = true;

    var payload = {
      customerId: els.customerId.value.trim(),
      customerName: els.customerName.value.trim(),
      simulateFailure: els.simulateFailure.checked,
      items: [{
        sku: els.sku.value.trim(),
        quantity: Number(els.quantity.value),
        unitPrice: Number(els.unitPrice.value)
      }]
    };

    var entry = {
      customerName: payload.customerName,
      orderTotal: currentTotal(),
      status: 'Submitting'
    };
    orders.unshift(entry);
    render();

    fetch(gateway + '/orders', {
      method: 'POST',
      headers: headers(),
      body: JSON.stringify(payload)
    })
      .then(function (r) {
        return readBody(r).then(function (body) {
          if (!r.ok) {
            entry.status = 'Failed';
            entry.error = describeFailure(r, body);
            return;
          }
          entry.orderId = body.orderId;
          entry.status = body.status;
          startPolling();
        });
      })
      .catch(function (err) {
        entry.status = 'Failed';
        entry.error = err.message;
      })
      .then(function () {
        els.submitBtn.disabled = false;
        render();
      });
  }

  function replay() {
    els.replayBtn.disabled = true;
    els.replayResult.hidden = false;
    els.replayResult.className = 'result';
    els.replayResult.textContent = 'Draining…';

    fetch(gateway + '/admin/replay?max=10', {
      method: 'POST',
      headers: headers(),
      body: '{}'
    })
      .then(function (r) {
        return readBody(r).then(function (body) {
          if (!r.ok) {
            els.replayResult.className = 'result error';
            els.replayResult.textContent = describeFailure(r, body);
            return;
          }
          els.replayResult.textContent =
            'drained ' + body.drained + ', resubmitted ' + body.resubmitted +
            (body.drained > body.resubmitted
              ? '\n(the difference was unreadable and discarded rather than looped back)'
              : '');
          startPolling();
        });
      })
      .catch(function (err) {
        els.replayResult.className = 'result error';
        els.replayResult.textContent = err.message;
      })
      .then(function () { els.replayBtn.disabled = false; });
  }

  // ---------------------------------------------------------------- wiring

  els.form.addEventListener('submit', submitOrder);
  els.quantity.addEventListener('input', refreshTotal);
  els.unitPrice.addEventListener('input', refreshTotal);
  els.replayBtn.addEventListener('click', replay);

  els.presetSmall.addEventListener('click', function () {
    els.customerId.value = 'CUST-SMALL';
    els.customerName.value = 'Below Threshold';
    els.quantity.value = 1;
    els.unitPrice.value = '50.00';
    els.simulateFailure.checked = false;
    refreshTotal();
  });

  els.presetLarge.addEventListener('click', function () {
    els.customerId.value = 'CUST-LARGE';
    els.customerName.value = 'Above Threshold';
    els.quantity.value = 1;
    els.unitPrice.value = '5000.00';
    els.simulateFailure.checked = false;
    refreshTotal();
  });

  if (cfg.appInsightsUrl) {
    els.portalLink.href = cfg.appInsightsUrl;
  } else {
    els.portalLink.remove();
  }

  els.configNote.textContent = gateway
    ? 'Gateway ' + gateway
    : 'No gateway configured. Run scripts/deploy-web.ps1 to generate config.js.';

  refreshTotal();
  render();
})();
