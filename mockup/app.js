/**
 * Ivy Tendril Static Prototype App Controller
 * Manages view switching, interactive promptware DAG graph, user feedback card blocking,
 * background jobs emulation, and tunnel live preview.
 */

class AppController {
  constructor() {
    this.currentStep = 1;
    this.currentView = 'plans';
    this.zoomLevel = 1.0;

    // Promptware DAG topology matching Jira example request:
    // 1. Promptware: Backend -> 2. Promptware: API -> 3. Promptware: Frontend Client
    // 4. App: Mobile App & 5. App: Web App (both depend on Frontend Client)
    // 6. Verifications (last step, depends on Mobile & Web)
    this.nodes = [
      {
        id: 'pw-backend',
        type: 'Promptware',
        name: 'create-backend',
        title: 'Backend Core Promptware',
        role: 'Promptware Exec',
        jobId: 'job-pw-backend-01',
        status: 'complete',
        x: 230, y: 40,
        cost: '$0.006', tokens: '1,800',
        prompt: 'Generate database schema, ORM mappings, and core entity repositories for Jira issues.',
        details: 'Compiled SQLite database schema and repository pattern classes.'
      },
      {
        id: 'pw-api',
        type: 'Promptware',
        name: 'create-backend-api',
        title: 'API Layer Promptware',
        role: 'Promptware Exec',
        jobId: 'job-pw-api-02',
        status: 'blocked',
        x: 230, y: 140,
        cost: '$0.010', tokens: '3,200',
        prompt: 'Build REST endpoints and real-time subscription router for issues and comments.',
        details: 'Waiting for user feedback on API protocol & workflow model preference.'
      },
      {
        id: 'pw-frontend',
        type: 'Promptware',
        name: 'create-frontend-client',
        title: 'Frontend Client Promptware',
        role: 'Promptware Exec',
        jobId: 'job-pw-frontend-03',
        status: 'pending',
        x: 230, y: 240,
        cost: '$0.014', tokens: '4,500',
        prompt: 'Create shared UI component library, Kanban board state, and API client SDK.',
        details: 'Pending completion of Backend API promptware.'
      },
      {
        id: 'app-mobile',
        type: 'App Build',
        name: 'build-mobile-app',
        title: 'Mobile App (iOS/Android)',
        role: 'App Artifact',
        jobId: 'job-app-mobile-04',
        status: 'pending',
        x: 100, y: 340,
        cost: '$0.008', tokens: '2,200',
        prompt: 'Compile React Native / iOS bundle targeting mobile viewport.',
        details: 'Depends on Frontend Client promptware SDK.'
      },
      {
        id: 'app-web',
        type: 'App Build',
        name: 'build-web-app',
        title: 'Web App (React/Vite)',
        role: 'App Artifact',
        jobId: 'job-app-web-05',
        status: 'pending',
        x: 360, y: 340,
        cost: '$0.009', tokens: '2,800',
        prompt: 'Build desktop web bundle with drag-and-drop Kanban interface.',
        details: 'Depends on Frontend Client promptware SDK.'
      },
      {
        id: 'verifications',
        type: 'Verifications',
        name: 'run-verifications',
        title: 'Final Verifications Suite',
        role: 'Validation Runner',
        jobId: 'job-verifications-06',
        status: 'pending',
        x: 230, y: 440,
        cost: '$0.003', tokens: '900',
        prompt: 'Execute end-to-end integration tests, type checks, and API contract verifications.',
        details: 'Final step: Runs after Mobile and Web apps complete.'
      }
    ];

    this.edges = [
      { from: 'pw-backend', to: 'pw-api', active: true, status: 'complete' },
      { from: 'pw-api', to: 'pw-frontend', active: false, status: 'blocked' },
      { from: 'pw-frontend', to: 'app-mobile', active: false, status: 'pending' },
      { from: 'pw-frontend', to: 'app-web', active: false, status: 'pending' },
      { from: 'app-mobile', to: 'verifications', active: false, status: 'pending' },
      { from: 'app-web', to: 'verifications', active: false, status: 'pending' }
    ];

    // Background jobs matching exact promptwares
    this.jobs = [
      { id: 'job-pw-backend-01', name: 'create-backend', type: 'Promptware', status: 'complete', deps: 'None', duration: '1.4s', tokens: '1,800' },
      { id: 'job-pw-api-02', name: 'create-backend-api', type: 'Promptware', status: 'blocked', deps: 'create-backend', duration: '3.2s', tokens: '3,200' },
      { id: 'job-pw-frontend-03', name: 'create-frontend-client', type: 'Promptware', status: 'pending', deps: 'create-backend-api', duration: '0.0s', tokens: '4,500' },
      { id: 'job-app-mobile-04', name: 'build-mobile-app', type: 'App Build', status: 'pending', deps: 'create-frontend-client', duration: '0.0s', tokens: '2,200' },
      { id: 'job-app-web-05', name: 'build-web-app', type: 'App Build', status: 'pending', deps: 'create-frontend-client', duration: '0.0s', tokens: '2,800' },
      { id: 'job-verifications-06', name: 'run-verifications', type: 'Verifications', status: 'pending', deps: 'build-mobile-app, build-web-app', duration: '0.0s', tokens: '900' }
    ];

    // File preview contents
    this.fileContents = {
      'schema.sql': `-- Generated Database Schema for Jira Prototype Clone
CREATE TABLE IF NOT EXISTS projects (
    id TEXT PRIMARY KEY,
    key TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS issues (
    id TEXT PRIMARY KEY,
    project_id TEXT REFERENCES projects(id),
    title TEXT NOT NULL,
    description TEXT,
    status TEXT CHECK(status IN ('todo', 'in_progress', 'review', 'done')),
    priority TEXT CHECK(priority IN ('low', 'medium', 'high', 'highest')),
    assignee TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);`,
      'routes.ts': `import { Router } from 'express';
export const router = Router();

// GET /api/v1/issues
router.get('/issues', async (req, res) => {
  const issues = await db.all('SELECT * FROM issues ORDER BY created_at DESC');
  res.json({ success: true, data: issues });
});

// POST /api/v1/issues/status
router.post('/issues/status', async (req, res) => {
  const { id, status } = req.body;
  await db.run('UPDATE issues SET status = ? WHERE id = ?', [status, id]);
  res.json({ success: true, updated: id });
});`,
      'KanbanBoard.tsx': `import React, { useState } from 'react';

export const KanbanBoard = ({ issues }) => {
  const columns = ['todo', 'in_progress', 'review', 'done'];
  return (
    <div className="grid grid-cols-4 gap-4 p-6">
      {columns.map(col => (
        <div key={col} className="bg-slate-900 rounded-lg p-4">
          <h3 className="font-bold uppercase text-slate-400 mb-3">{col}</h3>
          {issues.filter(i => i.status === col).map(issue => (
            <div key={issue.id} className="bg-slate-800 p-3 rounded mb-2 shadow">
              <span className="text-xs font-mono text-teal-400">{issue.id}</span>
              <h4 className="font-semibold text-sm mt-1">{issue.title}</h4>
            </div>
          ))}
        </div>
      ))}
    </div>
  );
};`,
      'package.json': `{
  "name": "jira-clone-tendril",
  "version": "1.0.0",
  "private": true,
  "scripts": {
    "dev": "vite",
    "build": "tsc && vite build",
    "start": "node server.js"
  },
  "dependencies": {
    "express": "^4.19.2",
    "react": "^18.3.1",
    "react-dom": "^18.3.1",
    "sqlite3": "^5.1.7"
  }
}`
    };

    this.init();
  }

  init() {
    this.renderJobsTable();
    this.renderConsoleLogs();
  }

  switchView(viewName) {
    this.currentView = viewName;

    document.querySelectorAll('.nav-item').forEach(item => item.classList.remove('active'));
    const activeNav = document.getElementById(`nav-${viewName}`) || document.getElementById('nav-chat');
    if (activeNav) activeNav.classList.add('active');

    document.querySelectorAll('.view-panel').forEach(panel => panel.classList.remove('active'));
    const targetPanel = document.getElementById(`view-${viewName}`);
    if (targetPanel) targetPanel.classList.add('active');
  }

  jumpToStep(stepNum) {
    this.currentStep = stepNum;
    this.updateStepperUI();

    if (stepNum === 1) {
      this.switchView('plans');
    } else if (stepNum === 2) {
      this.switchView('plans');
      this.ensurePromptSubmitted();
    } else if (stepNum === 3) {
      this.switchView('plans');
      this.ensurePromptSubmitted();
      this.ensureQuestionVisible();
    } else if (stepNum === 4) {
      this.switchView('drafts');
    }
  }

  updateStepperUI() {
    document.querySelectorAll('.step-chip').forEach((chip, index) => {
      if (index + 1 === this.currentStep) {
        chip.classList.add('active');
      } else {
        chip.classList.remove('active');
      }
    });
  }

  triggerNewPlan() {
    this.resetDemo();
    this.submitPrompt('make me Jira');
  }

  submitPrompt(promptText) {
    const chatInput = document.getElementById('chat-input');
    if (chatInput) chatInput.value = promptText;
    this.handleInputSubmit();
  }

  handleInputSubmit() {
    const chatInput = document.getElementById('chat-input');
    if (!chatInput || !chatInput.value.trim()) return;

    const userText = chatInput.value.trim();
    chatInput.value = '';

    this.appendUserMessage(userText);

    const quickPrompts = document.getElementById('quick-prompts');
    if (quickPrompts) quickPrompts.classList.add('hidden');

    setTimeout(() => {
      this.appendAgentThinking();
      this.showWorkflowCanvas();
      this.renderGraphSVG();
      this.jumpToStep(2);
    }, 400);
  }

  appendUserMessage(text) {
    const chatHistory = document.getElementById('chat-history');
    if (!chatHistory) return;

    const msg = document.createElement('div');
    msg.className = 'chat-message user';
    msg.innerHTML = `
      <div class="msg-avatar">U</div>
      <div class="msg-content">
        <div class="msg-author">You</div>
        <div class="msg-text">${this.escapeHTML(text)}</div>
      </div>
    `;
    chatHistory.appendChild(msg);
    chatHistory.scrollTop = chatHistory.scrollHeight;
  }

  appendAgentThinking() {
    const chatHistory = document.getElementById('chat-history');
    if (!chatHistory) return;

    const msg = document.createElement('div');
    msg.className = 'chat-message agent';
    msg.id = 'agent-response-thinking';
    msg.innerHTML = `
      <div class="msg-avatar">
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="12 2 2 7 12 12 22 7 12 2"></polygon><polyline points="2 17 12 22 22 17"></polyline><polyline points="2 12 12 17 22 12"></polyline></svg>
      </div>
      <div class="msg-content">
        <div class="msg-author">Tendril Orchestrator</div>
        <div class="msg-text">
          Decomposing prompt for <strong>Jira Clone Application</strong> into Promptware jobs & verifications...
          <div class="thinking-box">
            <div class="thinking-title">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 2v4M12 18v4M4.93 4.93l2.83 2.83M16.24 16.24l2.83 2.83M2 12h4M18 12h4M4.93 19.07l2.83-2.83M16.24 7.76l2.83-2.83"></path></svg>
              Scheduled Promptwares & Application Build Jobs
            </div>
            1. <code>create-backend</code> (Promptware &rarr; Database & ORM)<br>
            2. <code>create-backend-api</code> (Promptware &rarr; REST & Subscriptions)<br>
            3. <code>create-frontend-client</code> (Promptware &rarr; UI SDK)<br>
            4. <code>build-mobile-app</code> & <code>build-web-app</code> (Parallel App Builds)<br>
            5. <code>run-verifications</code> (Final Validation Step)
          </div>
        </div>
      </div>
    `;
    chatHistory.appendChild(msg);
    chatHistory.scrollTop = chatHistory.scrollHeight;

    setTimeout(() => {
      this.appendFeedbackQuestion();
      this.jumpToStep(3);
    }, 1200);
  }

  appendFeedbackQuestion() {
    const chatHistory = document.getElementById('chat-history');
    if (!chatHistory || document.getElementById('feedback-card-element')) return;

    const msg = document.createElement('div');
    msg.className = 'chat-message agent';
    msg.id = 'feedback-card-element';
    msg.innerHTML = `
      <div class="msg-avatar">
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path><line x1="12" y1="9" x2="12" y2="13"></line><line x1="12" y1="17" x2="12.01" y2="17"></line></svg>
      </div>
      <div class="msg-content">
        <div class="msg-author">Promptware: create-backend-api (Blocked)</div>
        <div class="feedback-card">
          <div class="feedback-header">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>
            User Feedback Required (Blocks Main Flow)
          </div>
          <div class="feedback-body">
            <p>Which issue tracking workflow model would you prefer for the <code>create-backend-api</code> promptware schema?</p>
            <div class="options-group">
              <button class="option-btn selected" onclick="appController.selectOption(this)">1. Hybrid Scrum & Kanban (Recommended)</button>
              <button class="option-btn" onclick="appController.selectOption(this)">2. Pure Agile Scrum with Sprint Backlog</button>
              <button class="option-btn" onclick="appController.selectOption(this)">3. Basic Task List with Custom Tags</button>
            </div>
            <button class="btn btn-primary btn-sm" onclick="appController.submitFeedback()">Submit Feedback & Resume Promptwares</button>
          </div>
        </div>
      </div>
    `;
    chatHistory.appendChild(msg);
    chatHistory.scrollTop = chatHistory.scrollHeight;

    const apiNode = this.nodes.find(n => n.id === 'pw-api');
    if (apiNode) apiNode.status = 'blocked';
    this.renderGraphSVG();
  }

  selectOption(btnElem) {
    const group = btnElem.closest('.options-group');
    if (!group) return;
    group.querySelectorAll('.option-btn').forEach(btn => btn.classList.remove('selected'));
    btnElem.classList.add('selected');
  }

  submitFeedback() {
    const card = document.getElementById('feedback-card-element');
    if (card) {
      card.querySelector('.feedback-card').innerHTML = `
        <div class="feedback-header" style="color: var(--accent-emerald);">
          ✓ Feedback Received & Promptwares Resumed
        </div>
        <div class="feedback-body">
          <p style="color: var(--text-secondary); margin: 0;">Selected: <strong>Hybrid Scrum & Kanban</strong>. <code>create-backend-api</code> promptware completed. Spawning client builds and verifications...</p>
        </div>
      `;
    }

    // Complete all promptwares, app builds, and verifications
    this.nodes.forEach(n => n.status = 'complete');
    this.edges.forEach(e => { e.active = true; e.status = 'complete'; });
    this.jobs.forEach(j => { j.status = 'complete'; j.duration = (Math.random() * 2 + 1).toFixed(1) + 's'; });

    this.renderJobsTable();
    this.renderGraphSVG();

    const badgeDrafts = document.getElementById('badge-drafts');
    if (badgeDrafts) {
      badgeDrafts.innerText = '1 Ready';
      badgeDrafts.className = 'nav-badge badge-ready';
    }

    this.jumpToStep(4);
  }

  showWorkflowCanvas() {
    const emptyCanvas = document.getElementById('empty-canvas-state');
    if (emptyCanvas) emptyCanvas.classList.add('hidden');

    const graphContainer = document.getElementById('workflow-graph-container');
    if (graphContainer) graphContainer.classList.remove('hidden');
  }

  renderGraphSVG() {
    const svgEdges = document.getElementById('svg-edges');
    const svgNodes = document.getElementById('svg-nodes');
    if (!svgEdges || !svgNodes) return;

    svgEdges.innerHTML = '';
    svgNodes.innerHTML = '';

    const nodeWidth = 190;
    const nodeHeight = 56;

    // Render Clean DAG Edges
    this.edges.forEach(edge => {
      const fromNode = this.nodes.find(n => n.id === edge.from);
      const toNode = this.nodes.find(n => n.id === edge.to);
      if (!fromNode || !toNode) return;

      const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      const startX = fromNode.x + nodeWidth / 2;
      const startY = fromNode.y + nodeHeight;
      const endX = toNode.x + nodeWidth / 2;
      const endY = toNode.y;

      const midY = (startY + endY) / 2;
      const d = `M ${startX} ${startY} C ${startX} ${midY}, ${endX} ${midY}, ${endX} ${endY}`;

      path.setAttribute('d', d);
      
      if (fromNode.status === 'complete' && toNode.status === 'complete') {
        path.setAttribute('class', 'edge-line edge-active');
        path.setAttribute('marker-end', 'url(#arrowhead-active)');
      } else if (fromNode.status === 'blocked' || toNode.status === 'blocked') {
        path.setAttribute('class', 'edge-line edge-blocked');
        path.setAttribute('marker-end', 'url(#arrowhead)');
      } else {
        path.setAttribute('class', 'edge-line');
        path.setAttribute('marker-end', 'url(#arrowhead)');
      }

      svgEdges.appendChild(path);
    });

    // Render Promptware & Task Nodes
    this.nodes.forEach(node => {
      const g = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      g.setAttribute('class', `svg-node node-${node.status}`);
      g.setAttribute('transform', `translate(${node.x}, ${node.y})`);
      g.onclick = () => this.inspectNode(node);

      let statusColor = '#4db6a0'; // Ivy teal default
      let statusLabel = 'RUNNING';
      if (node.status === 'complete') { statusColor = '#10b981'; statusLabel = 'COMPLETE'; }
      if (node.status === 'blocked') { statusColor = '#f59e0b'; statusLabel = 'BLOCKED'; }
      if (node.status === 'pending') { statusColor = '#64748b'; statusLabel = 'PENDING'; }

      g.innerHTML = `
        <rect class="node-card-rect" width="${nodeWidth}" height="${nodeHeight}" />
        <circle cx="16" cy="18" r="5" fill="${statusColor}" />
        <text class="node-type-badge" x="28" y="21">${node.type}</text>
        <text class="node-title-text" x="16" y="38">${node.name}</text>
        <text class="node-sub-text" x="16" y="50">${node.title}</text>
      `;

      svgNodes.appendChild(g);
    });
  }

  inspectNode(node) {
    const inspector = document.getElementById('node-inspector');
    const title = document.getElementById('inspector-title');
    const body = document.getElementById('inspector-body');

    if (!inspector || !title || !body) return;

    title.innerText = node.name;
    body.innerHTML = `
      <div style="margin-bottom: 8px;"><strong>Type:</strong> <span class="nav-badge badge-running">${node.type}</span></div>
      <div style="margin-bottom: 8px;"><strong>Job ID:</strong> <code class="font-mono">${node.jobId}</code></div>
      <div style="margin-bottom: 8px;"><strong>Status:</strong> <span class="status-tag tag-${node.status}">${node.status.toUpperCase()}</span></div>
      <div style="margin-bottom: 8px;"><strong>Prompt:</strong> <em>"${node.prompt}"</em></div>
      <div style="margin-bottom: 8px;"><strong>Tokens Used:</strong> ${node.tokens}</div>
      <div style="margin-bottom: 8px;"><strong>Estimated Cost:</strong> ${node.cost}</div>
      <div style="margin-top: 12px; font-size: 11px; color: var(--text-muted);">${node.details}</div>
      <button class="btn btn-primary btn-sm" style="margin-top: 14px; width: 100%; justify-content: center;" onclick="appController.jumpToJob('${node.jobId}')">
        Inspect Job in Jobs App &rarr;
      </button>
    `;

    inspector.classList.remove('hidden');
  }

  jumpToJob(jobId) {
    this.closeInspector();
    this.switchView('jobs');
    this.inspectJobLogs(jobId);
  }

  closeInspector() {
    const inspector = document.getElementById('node-inspector');
    if (inspector) inspector.classList.add('hidden');
  }

  renderJobsTable() {
    const tbody = document.getElementById('jobs-table-body');
    if (!tbody) return;

    tbody.innerHTML = this.jobs.map(job => `
      <tr id="row-${job.id}">
        <td class="font-mono">${job.id}</td>
        <td><strong>${job.name}</strong> <span class="nav-badge badge-neutral">${job.type}</span></td>
        <td><span class="status-tag tag-${job.status}">${job.status.toUpperCase()}</span></td>
        <td class="font-mono text-muted">${job.deps}</td>
        <td>${job.duration}</td>
        <td>${job.tokens}</td>
        <td><button class="btn btn-secondary btn-sm" onclick="appController.inspectJobLogs('${job.id}')">View Logs</button></td>
      </tr>
    `).join('');
  }

  inspectJobLogs(jobId) {
    const tag = document.getElementById('console-job-tag');
    if (tag) tag.innerText = jobId;
    this.renderConsoleLogs(jobId);
  }

  renderConsoleLogs(jobId = 'job-pw-api-02') {
    const consoleOutput = document.getElementById('console-output');
    if (!consoleOutput) return;

    consoleOutput.innerHTML = `
[00:00:00.05] [INFO] Executing promptware engine for ${jobId}...
[00:00:00.40] [INFO] Loaded promptware definition from ~/.tendril/Promptwares
[00:00:01.12] [DEBUG] Resolving dependencies and checking verification rules...
[00:00:02.10] [INFO] Promptware execution stream active.
[00:00:03.20] [STATUS] Job completed with 0 errors.
    `;
  }

  filterJobs(filter) {
    document.querySelectorAll('.filter-tab').forEach(t => t.classList.remove('active'));
    event.target.classList.add('active');

    if (filter === 'all') {
      this.renderJobsTable();
    } else {
      const filtered = this.jobs.filter(j => j.status === filter);
      const tbody = document.getElementById('jobs-table-body');
      if (tbody) {
        tbody.innerHTML = filtered.map(job => `
          <tr>
            <td class="font-mono">${job.id}</td>
            <td><strong>${job.name}</strong> <span class="nav-badge badge-neutral">${job.type}</span></td>
            <td><span class="status-tag tag-${job.status}">${job.status.toUpperCase()}</span></td>
            <td class="font-mono text-muted">${job.deps}</td>
            <td>${job.duration}</td>
            <td>${job.tokens}</td>
            <td><button class="btn btn-secondary btn-sm" onclick="appController.inspectJobLogs('${job.id}')">View Logs</button></td>
          </tr>
        `).join('');
      }
    }
  }

  selectFile(fileName) {
    document.querySelectorAll('.file-item').forEach(item => item.classList.remove('active'));
    event.target.classList.add('active');

    const preview = document.getElementById('code-preview-content');
    if (preview && this.fileContents[fileName]) {
      preview.textContent = this.fileContents[fileName];
    }
  }

  spinUpTunnel() {
    const modal = document.getElementById('tunnel-modal');
    if (modal) modal.classList.remove('hidden');
  }

  closeTunnelModal() {
    const modal = document.getElementById('tunnel-modal');
    if (modal) modal.classList.add('hidden');
  }

  resetDemo() {
    this.currentStep = 1;
    this.updateStepperUI();
    this.switchView('plans');

    const chatHistory = document.getElementById('chat-history');
    if (chatHistory) {
      chatHistory.innerHTML = `
        <div class="chat-message agent">
          <div class="msg-avatar">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="12 2 2 7 12 12 22 7 12 2"></polygon><polyline points="2 17 12 22 22 17"></polyline><polyline points="2 12 12 17 22 12"></polyline></svg>
          </div>
          <div class="msg-content">
            <div class="msg-author">Tendril Orchestrator</div>
            <div class="msg-text">
              Welcome! Describe the project or application you would like me to build. I will decompose your prompt into promptware execution steps, visualize the live state DAG on the right, and compile a draft repository.
            </div>
          </div>
        </div>
      `;
    }

    const quickPrompts = document.getElementById('quick-prompts');
    if (quickPrompts) quickPrompts.classList.remove('hidden');

    const graphContainer = document.getElementById('workflow-graph-container');
    if (graphContainer) graphContainer.classList.add('hidden');

    const emptyCanvas = document.getElementById('empty-canvas-state');
    if (emptyCanvas) emptyCanvas.classList.remove('hidden');

    this.nodes[1].status = 'blocked';
    this.nodes[2].status = 'pending';
    this.nodes[3].status = 'pending';
    this.nodes[4].status = 'pending';
    this.nodes[5].status = 'pending';
    this.renderGraphSVG();
  }

  ensurePromptSubmitted() {
    if (!document.getElementById('agent-response-thinking')) {
      this.submitPrompt('make me Jira');
    }
  }

  ensureQuestionVisible() {
    if (!document.getElementById('feedback-card-element')) {
      this.appendFeedbackQuestion();
    }
  }

  zoomGraph(factor) {
    this.zoomLevel *= factor;
    const svg = document.getElementById('workflow-svg');
    if (svg) svg.style.transform = `scale(${this.zoomLevel})`;
  }

  resetGraphZoom() {
    this.zoomLevel = 1.0;
    const svg = document.getElementById('workflow-svg');
    if (svg) svg.style.transform = `scale(1.0)`;
  }

  escapeHTML(str) {
    return str.replace(/[&<>'"]/g, 
      tag => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[tag] || tag)
    );
  }
}

// Global instance
const appController = new AppController();
