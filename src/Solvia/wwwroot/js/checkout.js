(function () {
    'use strict';
    const checkout = document.getElementById('checkout-payment');
    const container = document.getElementById('paypal-button-container');
    if (!checkout || !container) return;

    const feedback = document.getElementById('checkout-feedback');
    const order = document.getElementById('paypal-order-id');
    const capture = document.getElementById('paypal-capture-id');
    const state = document.getElementById('payment-state');
    const applicationId = checkout.dataset.applicationId;
    const doneUrl = '/Checkout/Done/' + encodeURIComponent(applicationId);
    let lastError = '';

    function show(message, error) {
        feedback.textContent = message;
        feedback.className = 'alert mt-3 ' + (error ? 'alert-danger' : 'alert-info');
        feedback.hidden = false;
        if (error) {
            lastError = message;
            feedback.focus();
        }
    }

    async function post(url, body) {
        let response;
        try {
            response = await fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': document.querySelector('#plain-pay-form input[name="__RequestVerificationToken"]').value
                },
                body: JSON.stringify(body)
            });
        } catch {
            throw new Error('Connection interrupted. Payment is not confirmed here. Check your permit status before retrying.');
        }
        let data;
        try { data = await response.json(); }
        catch { throw new Error('Payment service returned an unreadable response. Check your permit status before retrying.'); }
        if (!response.ok) {
            const details = [data.code, data.debugId ? 'Support reference: ' + data.debugId : ''].filter(Boolean).join(' · ');
            throw new Error((data.error || 'Payment service could not confirm this request.') + (details ? ' (' + details + ')' : ''));
        }
        return data;
    }

    if (!window.paypal || typeof window.paypal.Buttons !== 'function') {
        show('PayPal buttons could not load. Use the PayPal redirect button below to continue.', true);
        return;
    }
    try {
        const buttons = window.paypal.Buttons({
            createOrder: async function () {
                lastError = '';
                show('Preparing your PayPal order…', false);
                try {
                    const data = await post('/api/paypal/orders', { permitApplicationId: applicationId });
                    if (data.status === 'not-required' || data.status === 'already-paid') {
                        window.location.assign(doneUrl);
                        return null;
                    }
                    if (!data.id) throw new Error('PayPal did not return an order ID. Payment is not confirmed.');
                    order.textContent = data.id;
                    state.textContent = 'Order created — awaiting buyer approval and capture.';
                    show('Approve this order in PayPal Sandbox using your Personal buyer test account.', false);
                    return data.id;
                } catch (error) {
                    show(error.message, true);
                    throw error;
                }
            },
            onApprove: async function (data) {
                lastError = '';
                show('Confirming payment with PayPal…', false);
                try {
                    const result = await post('/api/paypal/capture', { orderId: data.orderID });
                    order.textContent = result.orderId || data.orderID;
                    if (result.captureId) capture.textContent = result.captureId;
                    if (result.completed !== true || result.status !== 'COMPLETED' || !result.captureId) {
                        state.textContent = 'Payment not confirmed — ' + (result.status || 'unknown status') + '.';
                        show('PayPal payment is ' + (result.status || 'unconfirmed') + '. This permit is not active. Check your permit status before retrying.', true);
                        return;
                    }
                    window.location.assign(doneUrl);
                } catch (error) {
                    show(error.message, true);
                }
            },
            onCancel: function () {
                show('PayPal approval was cancelled. Payment is not confirmed. You can resume the same order below.', false);
            },
            onError: function () {
                show(lastError || 'PayPal could not finish checkout. Payment is not confirmed. Check your permit status or use the redirect button below.', true);
            }
        });
        Promise.resolve(buttons.render('#paypal-button-container')).catch(() =>
            show('PayPal buttons could not load. Use the PayPal redirect button below to continue.', true));
    } catch {
        show('PayPal buttons could not load. Use the PayPal redirect button below to continue.', true);
    }
})();
