(function () {
    'use strict';
    const checkout = document.getElementById('checkout-payment');
    const container = document.getElementById('paypal-button-container');
    if (!checkout || !container) return;

    const feedback = document.getElementById('checkout-feedback');
    const fallback = document.getElementById('paypal-redirect-fallback');
    const order = document.getElementById('paypal-order-id');
    const capture = document.getElementById('paypal-capture-id');
    const state = document.getElementById('payment-state');
    const applicationId = checkout.dataset.applicationId;
    const doneUrl = '/Checkout/Done/' + encodeURIComponent(applicationId);
    let lastError = '';
    let optionsSettled = false;
    let renderStarted = false;
    let loadTimer;
    let renderTimer;
    const optionsTimeout = 12000;
    const loadingMessage = 'Loading PayPal payment options…';

    function showFallback() {
        if (optionsSettled) return;
        optionsSettled = true;
        window.clearTimeout(loadTimer);
        window.clearTimeout(renderTimer);
        container.hidden = true;
        if (fallback) fallback.hidden = false;
        show('PayPal payment options could not load. Continue to PayPal below to finish checkout.', true);
    }

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

    function renderButtons() {
        if (optionsSettled || renderStarted) return;
        window.clearTimeout(loadTimer);
        if (!window.paypal || typeof window.paypal.Buttons !== 'function') {
            showFallback();
            return;
        }
        renderStarted = true;
        try {
        const buttons = window.paypal.Buttons({
            style: { layout: 'vertical', color: 'gold', shape: 'rect', label: 'paypal' },
            createOrder: async function () {
                lastError = '';
                show('Preparing your PayPal order…', false);
                try {
                    const data = await post('/api/paypal/orders', {
                        permitApplicationId: applicationId,
                        reviewedSnapshot: checkout.dataset.reviewedSnapshot
                    });
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
                show('PayPal approval was cancelled. Payment is not confirmed. Choose PayPal again to resume the same order.', false);
            },
            onError: function () {
                show(lastError || 'PayPal could not finish checkout. Payment is not confirmed. Check your permit status before trying again.', true);
            }
        });
        if (typeof buttons.isEligible === 'function' && !buttons.isEligible()) {
            showFallback();
            return;
        }
            renderTimer = window.setTimeout(showFallback, optionsTimeout);
            Promise.resolve(buttons.render('#paypal-button-container')).then(function () {
                window.clearTimeout(renderTimer);
                // A slow provider must not replace the fallback after timeout
                // or leave two payment actions visible.
                if (optionsSettled) return;
                optionsSettled = true;
                if (feedback.textContent === loadingMessage) feedback.hidden = true;
            }, showFallback);
        } catch {
            showFallback();
        }
    }

    show(loadingMessage, false);
    if (window.paypal && typeof window.paypal.Buttons === 'function') {
        renderButtons();
    } else if (checkout.dataset.paypalSdkUrl) {
        const sdk = document.createElement('script');
        sdk.id = 'paypal-sdk';
        sdk.src = checkout.dataset.paypalSdkUrl;
        sdk.async = true;
        sdk.addEventListener('load', renderButtons, { once: true });
        sdk.addEventListener('error', showFallback, { once: true });
        loadTimer = window.setTimeout(showFallback, optionsTimeout);
        document.head.append(sdk);
    } else {
        showFallback();
    }
})();
