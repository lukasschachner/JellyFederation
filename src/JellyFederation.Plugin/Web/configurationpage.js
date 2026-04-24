(function () {
    'use strict';

    if (window.__jellyFederationConfigScriptInitialized === true) {
        return;
    }

    window.__jellyFederationConfigScriptInitialized = true;

    var pluginId = 'e5c0cda1-805e-41e2-9654-e17143dc31a1';
    var pageId = 'JellyFederationConfigPage';
    var formId = 'JellyFederationConfigForm';
    var defaultHolePunchPort = 0;
    var defaultLargeFileQuicThresholdBytes = 536870912;
    var observerTimeoutMs = 30000;

    function getPage(element) {
        return element && element.closest ? element.closest('#' + pageId) : document.getElementById(pageId);
    }

    function setInputValue(page, selector, value) {
        var input = page.querySelector(selector);
        if (input) {
            input.value = value === undefined || value === null ? '' : value;
        }
    }

    function getTrimmedValue(form, selector) {
        var input = form.querySelector(selector);
        return input ? input.value.trim() : '';
    }

    function setChecked(page, selector, value) {
        var input = page.querySelector(selector);
        if (input) {
            input.checked = value;
        }
    }

    function parseNonNegativeInt(value, fallback) {
        var parsed = parseInt(value, 10);
        return Number.isFinite(parsed) && parsed >= 0 ? parsed : fallback;
    }

    function parsePositiveInt(value, fallback) {
        var parsed = parseInt(value, 10);
        return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
    }

    function hideLoadingMessage() {
        if (window.Dashboard && Dashboard.hideLoadingMsg) {
            Dashboard.hideLoadingMsg();
        }
    }

    function showLoadingMessage() {
        if (window.Dashboard && Dashboard.showLoadingMsg) {
            Dashboard.showLoadingMsg();
        }
    }

    function showAlert(title, text) {
        if (window.Dashboard && Dashboard.alert) {
            Dashboard.alert({ title: title, text: text });
        } else {
            window.alert(text);
        }
    }

    function processSaveResult(result) {
        if (window.Dashboard && Dashboard.processPluginConfigurationUpdateResult) {
            Dashboard.processPluginConfigurationUpdateResult(result);
            return;
        }

        showAlert('JellyFederation', 'Settings saved.');
    }

    function loadConfig(page) {
        if (!page || !window.ApiClient || !ApiClient.getPluginConfiguration) {
            return Promise.resolve();
        }

        showLoadingMessage();

        return ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            config = config || {};

            setInputValue(page, '#federationServerUrl', config.FederationServerUrl);
            setInputValue(page, '#serverId', config.ServerId);
            setInputValue(page, '#apiKey', config.ApiKey);
            setInputValue(page, '#serverName', config.ServerName);
            setInputValue(page, '#publicJellyfinUrl', config.PublicJellyfinUrl);
            setInputValue(page, '#downloadDirectory', config.DownloadDirectory);
            setInputValue(page, '#stunServer', config.StunServer);
            setInputValue(page, '#turnServer', config.TurnServer);
            setInputValue(page, '#turnUsername', config.TurnUsername);
            setInputValue(page, '#turnCredential', config.TurnCredential);
            setInputValue(page, '#overridePublicIp', config.OverridePublicIp);
            setInputValue(page, '#holePunchPort', config.HolePunchPort || defaultHolePunchPort);
            setChecked(page, '#preferQuicForLargeFiles', config.PreferQuicForLargeFiles !== false);
            setInputValue(page, '#largeFileQuicThresholdBytes', config.LargeFileQuicThresholdBytes || defaultLargeFileQuicThresholdBytes);
        }).catch(function (err) {
            console.error('[JellyFederation] Failed to load config:', err);
            showAlert('JellyFederation', 'Failed to load settings. Check the browser console for details.');
        }).then(function () {
            hideLoadingMessage();
        });
    }

    function saveConfig(form) {
        if (!form || !window.ApiClient || !ApiClient.getPluginConfiguration || !ApiClient.updatePluginConfiguration) {
            showAlert('JellyFederation', 'Jellyfin API client is not available yet. Please reopen the settings page and try again.');
            return Promise.resolve();
        }

        showLoadingMessage();

        return ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            config = config || {};

            config.FederationServerUrl = getTrimmedValue(form, '#federationServerUrl');
            config.ServerId = getTrimmedValue(form, '#serverId');
            config.ApiKey = getTrimmedValue(form, '#apiKey');
            config.ServerName = getTrimmedValue(form, '#serverName');
            config.PublicJellyfinUrl = getTrimmedValue(form, '#publicJellyfinUrl');
            config.DownloadDirectory = getTrimmedValue(form, '#downloadDirectory');
            config.StunServer = getTrimmedValue(form, '#stunServer');
            config.TurnServer = getTrimmedValue(form, '#turnServer');
            config.TurnUsername = getTrimmedValue(form, '#turnUsername');
            config.TurnCredential = getTrimmedValue(form, '#turnCredential');
            config.OverridePublicIp = getTrimmedValue(form, '#overridePublicIp');
            config.HolePunchPort = parseNonNegativeInt(getTrimmedValue(form, '#holePunchPort'), defaultHolePunchPort);

            var preferQuic = form.querySelector('#preferQuicForLargeFiles');
            config.PreferQuicForLargeFiles = !!(preferQuic && preferQuic.checked);
            config.LargeFileQuicThresholdBytes = parsePositiveInt(
                getTrimmedValue(form, '#largeFileQuicThresholdBytes'),
                defaultLargeFileQuicThresholdBytes);

            return ApiClient.updatePluginConfiguration(pluginId, config);
        }).then(function (result) {
            processSaveResult(result);
        }).catch(function (err) {
            var message = err && (err.message || JSON.stringify(err)) || 'Unknown error';
            console.error('[JellyFederation] Failed to save config:', err);
            showAlert('JellyFederation', 'Save failed — ' + message);
        }).then(function () {
            hideLoadingMessage();
        });
    }

    function handlePageShow(event) {
        var page = event && event.target ? getPage(event.target) : null;
        if (page && page.id === pageId) {
            bindPage(page, false);
            loadConfig(page);
        }
    }

    function handleSubmit(event) {
        var form = event && event.target && event.target.id === formId ? event.target : null;
        if (!form) {
            return;
        }

        event.preventDefault();
        saveConfig(form);
    }

    function bindPage(page, loadImmediately) {
        if (!page || page.dataset.jellyFederationBound === 'true') {
            return;
        }

        page.dataset.jellyFederationBound = 'true';
        page.addEventListener('pageshow', function () {
            loadConfig(page);
        });

        if (loadImmediately) {
            loadConfig(page);
        }
    }

    function findAndBind(loadImmediately) {
        var pages = document.querySelectorAll('#' + pageId);
        Array.prototype.forEach.call(pages, function (page) {
            bindPage(page, loadImmediately);
        });

        return pages.length > 0;
    }

    function initialize() {
        document.addEventListener('pageshow', handlePageShow);
        document.addEventListener('submit', handleSubmit, true);

        if (findAndBind(true)) {
            return;
        }

        var observer = new MutationObserver(function () {
            if (findAndBind(true)) {
                observer.disconnect();
                window.clearTimeout(observerTimeoutId);
            }
        });

        var observerRoot = document.body || document.documentElement;
        observer.observe(observerRoot, { childList: true, subtree: true });

        var observerTimeoutId = window.setTimeout(function () {
            observer.disconnect();
        }, observerTimeoutMs);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialize, { once: true });
    } else {
        initialize();
    }
}());
